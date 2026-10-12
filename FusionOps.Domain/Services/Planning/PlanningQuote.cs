using System.Security.Cryptography;
using System.Text.Json;
using FusionOps.Domain.Enumerations;
using FusionOps.Domain.Shared;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Services.Planning;

public sealed record QuotedCandidate(long Revision, ResourceCandidate Resource);
public sealed record QuoteEvidence(PlanningRequest Request, IReadOnlyList<QuotedCandidate> Candidates, PlanningResult Result);
public sealed record PlanningQuoteBinding(Guid Id, string Digest, decimal Amount, Currency Currency, DateTimeOffset ExpiresAt)
{
    public Money TotalCost => new(Amount, Currency);
}

/// <summary>Immutable, self-contained planning evidence; consumption is the only mutable field.</summary>
public sealed class PlanningQuote
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private PlanningQuote() { }
    public Guid Id { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public string Digest { get; private set; } = string.Empty;
    public string EvidenceJson { get; private set; } = string.Empty;
    public Guid? ConsumedByWorkflowId { get; private set; }
    public QuoteEvidence Evidence => JsonSerializer.Deserialize<QuoteEvidence>(EvidenceJson, Json)
        ?? throw new InvalidOperationException("Quote evidence is missing");
    public PlanningQuoteBinding Binding
    {
        get
        {
            var cost = Evidence.Result.TotalCost;
            return new(Id, Digest, cost.Amount, cost.Currency, ExpiresAt);
        }
    }

    public static PlanningQuote Create(QuoteEvidence evidence, string actor, DateTimeOffset now, int validitySeconds)
    {
        if (validitySeconds is < 15 or > 900) throw new DomainException("Quote validity must be 15–900 seconds");
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 200) throw new DomainException("A bounded quote actor is required");
        // PostgreSQL timestamps retain microseconds; preserve an identical binding across both stores.
        var timestamp = new DateTimeOffset(now.UtcTicks / 10 * 10, TimeSpan.Zero);
        var quote = new PlanningQuote { Id = Guid.NewGuid(), CreatedBy = actor, CreatedAt = timestamp, ExpiresAt = timestamp.AddSeconds(validitySeconds) };
        quote.EvidenceJson = JsonSerializer.Serialize(evidence, Json);
        quote.Digest = quote.CalculateDigest();
        return quote;
    }

    public void VerifyBinding(PlanningQuoteBinding binding)
    {
        VerifyIntegrity();
        if (binding != Binding) throw new QuoteAdmissionException("quote_binding_mismatch");
    }
    public void VerifyIntegrity()
    {
        if (Digest != CalculateDigest()) throw new QuoteAdmissionException("quote_evidence_invalid");
    }
    private string CalculateDigest() => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        Id, CreatedBy, CreatedAt, ExpiresAt, EvidenceJson
    }, Json)));
    public void Consume(Guid workflowId, DateTimeOffset now)
    {
        if (workflowId == Guid.Empty) throw new DomainException("A workflow ID is required");
        if (ConsumedByWorkflowId == workflowId) return;
        if (ConsumedByWorkflowId is not null) throw new QuoteAdmissionException("quote_consumed");
        if (now >= ExpiresAt) throw new QuoteAdmissionException("quote_expired");
        if (!Evidence.Result.IsComplete || Evidence.Result.Assignments.Count == 0) throw new QuoteAdmissionException("quote_incomplete");
        ConsumedByWorkflowId = workflowId;
    }
}

public sealed class QuoteAdmissionException(string code) : DomainException(code)
{
    public string Code { get; } = code;
}
