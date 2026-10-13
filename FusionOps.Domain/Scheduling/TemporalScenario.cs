using System.Security.Cryptography;
using System.Text.Json;
using FusionOps.Domain.Events;
using FusionOps.Domain.Projects;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.Tenancy;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Scheduling;

public sealed record ResourceUnavailability(Guid ResourceId, TimeRange Period);
public sealed record ScenarioAssumptions(IReadOnlyList<ResourceUnavailability> Unavailable,
    IReadOnlyDictionary<string, int> AdditionalStock, IReadOnlyDictionary<string, int> AdditionalLicenseSeats)
{
    public static ScenarioAssumptions None => new([], new Dictionary<string, int>(), new Dictionary<string, int>());
    public bool IsHypothetical => Unavailable.Count > 0 || AdditionalStock.Count > 0 || AdditionalLicenseSeats.Count > 0;
    public ScenarioAssumptions Normalize(IReadOnlyList<Guid> candidates)
    {
        if (Unavailable is null || Unavailable.Count > 200 || Unavailable.Any(u => u is null || u.ResourceId == Guid.Empty
                || !candidates.Contains(u.ResourceId) || u.Period is null) || AdditionalStock is null || AdditionalLicenseSeats is null)
            throw new TemporalModelException("scenario_assumptions_invalid");
        foreach (var additions in new[] { AdditionalStock, AdditionalLicenseSeats })
            if (additions.Count > 200 || additions.Any(p => !ProjectDefinitionCodec.Text(p.Key, 128) || p.Value is < 1 or > 1_000_000))
                throw new TemporalModelException("scenario_assumptions_invalid");
        return new(Unavailable.OrderBy(u => u.ResourceId).ThenBy(u => u.Period.Start).ThenBy(u => u.Period.End).ToArray(),
            AdditionalStock.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value),
            AdditionalLicenseSeats.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value));
    }
    public TemporalPlanningInput Apply(TemporalPlanningInput source)
    {
        var normalized = Normalize(source.Candidates.Select(c => c.Id).ToArray());
        var stock = source.Stock.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        foreach (var p in normalized.AdditionalStock)
        {
            if (!stock.ContainsKey(p.Key) || source.MaterialUnitCosts is not { } prices || !prices.ContainsKey(p.Key))
                throw new TemporalModelException("scenario_material_price_required");
            stock[p.Key] = checked(stock[p.Key] + p.Value);
        }
        var licenses = source.LicenseCapacity.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        foreach (var p in normalized.AdditionalLicenseSeats)
        {
            if (!licenses.ContainsKey(p.Key)) throw new TemporalModelException("scenario_license_source_required");
            licenses[p.Key] = checked(licenses[p.Key] + p.Value);
        }
        var applied = source with { Candidates = source.Candidates.Select(c => c with
            { BusyPeriods = c.BusyPeriods.Concat(normalized.Unavailable.Where(u => u.ResourceId == c.Id).Select(u => u.Period)).ToArray() }).ToArray(),
            Stock = stock, LicenseCapacity = licenses };
        TemporalModel.Validate(applied); return applied;
    }
}
public sealed record ResourceObservation(Guid Id, long Revision);
public sealed record StockObservation(string Sku, Guid? ItemId, int Quantity, Money? UnitCost);
public sealed record LicenseObservation(string Product, Guid? PoolId, long? Version, int TotalSeats, int AllocatedSeats);
public sealed record TemporalSourceManifest(int SchemaVersion, long ProjectVersion, long DefinitionVersion, string DefinitionDigest,
    DateTimeOffset ProjectObservedAt, DateTimeOffset WorkforceObservedAt,
    IReadOnlyList<ResourceObservation> Resources, IReadOnlyList<StockObservation> Stock, IReadOnlyList<LicenseObservation> Licenses);
public sealed record TemporalScenarioEvidence(TemporalSourceManifest Source, TemporalPlanningInput Basis, ScenarioAssumptions Assumptions,
    TemporalPlanningInput Input, TemporalPlanningResult Result, TemporalValidation Validation);

public sealed class TemporalScenario : TenantOwnedEntity, IHasDomainEvents
{
    private readonly List<IDomainEvent> _events = [];
    private TemporalScenario() { }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public long DefinitionVersion { get; private set; }
    public long ProjectVersion { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public string RequestHash { get; private set; } = string.Empty;
    public string Digest { get; private set; } = string.Empty;
    public string EvidenceJson { get; private set; } = string.Empty;
    public bool IsComplete { get; private set; }
    public bool SearchComplete { get; private set; }
    public bool IsHypothetical { get; private set; }
    public decimal TotalCost { get; private set; }
    public DateTimeOffset? FinishAt { get; private set; }
    public TemporalScenarioEvidence Evidence => JsonSerializer.Deserialize<TemporalScenarioEvidence>(EvidenceJson, TemporalInputCodec.Json)
        ?? throw new TemporalModelException("scenario_evidence_missing");
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _events.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _events.Add(domainEvent);
    public void ClearDomainEvents() => _events.Clear();
    public static TemporalScenario Create(Guid id, Guid tenant, string actor, string requestHash, TemporalScenarioEvidence evidence, DateTimeOffset now)
    {
        if (id == Guid.Empty || !ProjectDefinitionCodec.Text(actor, 200) || requestHash.Length != 64)
            throw new TemporalModelException("scenario_identity_invalid");
        var scenario = new TemporalScenario { Id = id, ProjectId = evidence.Input.ProjectId, ProjectVersion = evidence.Source.ProjectVersion,
            DefinitionVersion = evidence.Source.DefinitionVersion, Actor = actor, RequestHash = requestHash,
            CreatedAt = new(now.UtcTicks / 10 * 10, TimeSpan.Zero), IsComplete = evidence.Result.IsComplete, SearchComplete = evidence.Result.SearchComplete,
            IsHypothetical = evidence.Assumptions.IsHypothetical, TotalCost = evidence.Result.TotalCost, FinishAt = evidence.Result.FinishAt,
            EvidenceJson = JsonSerializer.Serialize(evidence, TemporalInputCodec.Json) };
        if (scenario.EvidenceJson.Length > 8_000_000) throw new TemporalModelException("scenario_evidence_limits");
        scenario.BindTenant(tenant); scenario.Digest = scenario.CalculateDigest(); scenario.VerifyEvidence();
        scenario.AddDomainEvent(new TemporalScenarioCalculated(id, scenario.ProjectId, scenario.DefinitionVersion, scenario.Digest, scenario.IsComplete)
        { OccurredOn = scenario.CreatedAt }); return scenario;
    }
    public void VerifyEvidence()
    {
        if (Digest != CalculateDigest()) throw new TemporalModelException("scenario_evidence_invalid");
        var evidence = Evidence;
        var validation = new TemporalPlanValidator().Validate(evidence.Input, evidence.Result.Tasks, requireComplete: false);
        if (evidence.Source.SchemaVersion != 1 || evidence.Basis.ProjectId != ProjectId || evidence.Input.ProjectId != ProjectId
            || evidence.Result.ProjectId != ProjectId || evidence.Result.Algorithm != TemporalPlanner.AlgorithmFor(evidence.Input)
            || evidence.Source.ProjectVersion != ProjectVersion || evidence.Source.DefinitionVersion != DefinitionVersion
            || evidence.Source.DefinitionDigest != ProjectDefinitionCodec.Digest(TenantId, ProjectId, DefinitionVersion, ProjectDefinitionCodec.Canonicalize(evidence.Basis.Definition))
            || TemporalInputCodec.Write(evidence.Assumptions.Apply(evidence.Basis)) != TemporalInputCodec.Write(evidence.Input)
            || !validation.IsFeasible || !evidence.Validation.IsFeasible || validation.IsComplete != IsComplete || validation.IsComplete != evidence.Result.IsComplete
            || validation.ComputedCost != TotalCost || TotalCost != evidence.Result.TotalCost || SearchComplete != evidence.Result.SearchComplete
            || IsHypothetical != evidence.Assumptions.IsHypothetical || FinishAt != evidence.Result.FinishAt
            || FinishAt != (evidence.Result.Tasks.Count == 0 ? (DateTimeOffset?)null : evidence.Result.Tasks.Max(t => t.Period.End))
            || evidence.Result.ExploredNodes < 0 || evidence.Result.ExploredNodes > evidence.Input.MaxSearchNodes
            || evidence.Validation.ComputedCost != validation.ComputedCost || evidence.Validation.IsComplete != validation.IsComplete)
            throw new TemporalModelException("scenario_evidence_invalid");
    }
    private string CalculateDigest() => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    { Schema = 1, TenantId, Id, ProjectId, DefinitionVersion, ProjectVersion, Actor, CreatedAt, RequestHash, EvidenceJson }, TemporalInputCodec.Json)));
}
public sealed class ScenarioSubmissionConflictException() : FusionOps.Domain.Shared.DomainException("The scenario ID has another original submission");
