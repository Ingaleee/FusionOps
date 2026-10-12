using FusionOps.Domain.Catalog;
using FusionOps.Domain.Enumerations;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Application.Planning;

public sealed record CatalogEdit(string Name, Money HourRate, IReadOnlyDictionary<string, int> Skills, string? EquipmentType, bool IsActive = true);
public sealed record QuoteRequest(Guid ProjectId, TimeRange Period, Currency Currency, IReadOnlyList<DemandSlot> Slots,
    IReadOnlyList<Guid> CandidateIds, int ValiditySeconds = 300);
public interface IResourceCatalogStore
{
    Task<CatalogResource> RegisterAsync(Guid id, ResourceKind kind, CatalogEdit edit, CancellationToken ct);
    Task<CatalogResource?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<CatalogResource>> ListAsync(Guid? after, int limit, CancellationToken ct);
    Task<CatalogResource?> ReviseAsync(Guid id, long expectedRevision, CatalogEdit edit, CancellationToken ct);
}
public interface IPlanningQuoteStore
{
    Task<PlanningQuote> CreateAsync(QuoteRequest request, CancellationToken ct);
    Task<PlanningQuote?> GetAsync(Guid id, CancellationToken ct);
}
