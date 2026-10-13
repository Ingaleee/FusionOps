using FusionOps.Domain.Enumerations;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.Shared;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Presentation.Modules;

public static class PlanningEndpoints
{
    public static IEndpointRouteBuilder MapPlanningEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var endpoint = endpoints.MapPost("/api/v1/planning/preview", (PlanRequest dto, ResourcePlanner planner, CancellationToken ct) =>
        {
            var currency = Currency.All.SingleOrDefault(c => string.Equals(c.Name, dto.Currency, StringComparison.OrdinalIgnoreCase));
            if (currency == default) throw new DomainException("Use USD, EUR or RUB as the planning currency");
            if (dto.Slots is null || dto.Candidates is null || dto.Candidates.Any(c => c is null))
                throw new DomainException("Slots and candidates are required and cannot contain null entries");
            var request = new PlanningRequest(dto.ProjectId, new TimeRange(dto.From, dto.To), currency, dto.Slots,
                dto.Candidates.Select(c => new ResourceCandidate(c.Id, c.Name, c.Kind, new Money(c.HourlyRate, currency),
                    c.Skills ?? new Dictionary<string, int>(), c.BusyPeriods ?? [], c.EquipmentType)).ToArray());
            return Results.Ok(planner.Plan(request, ct));
        }).WithName("PreviewResourcePlan").Produces<PlanningResult>().ProducesProblem(422);
        if (!endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
            endpoint.RequireAuthorization("ManageResources");
        return endpoints;
    }

    public sealed record PlanRequest(Guid ProjectId, DateTimeOffset From, DateTimeOffset To, string Currency,
        IReadOnlyList<DemandSlot> Slots, IReadOnlyList<CandidateDto> Candidates);
    public sealed record CandidateDto(Guid Id, string Name, ResourceKind Kind, decimal HourlyRate,
        IReadOnlyDictionary<string, int>? Skills, IReadOnlyList<TimeRange>? BusyPeriods, string? EquipmentType);
}
