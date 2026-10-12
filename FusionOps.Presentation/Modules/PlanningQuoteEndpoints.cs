using FusionOps.Application.Planning;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Presentation.Modules;

public static class PlanningQuoteEndpoints
{
    public static IEndpointRouteBuilder MapPlanningQuoteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/api/v1/planning/quotes").WithTags("Authoritative planning");
        if (!endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment()) routes.RequireAuthorization("ManageResources");
        routes.MapPost("/", async (QuoteDto dto, IPlanningQuoteStore store, HttpResponse response, CancellationToken ct) =>
        {
            var quote = await store.CreateAsync(new(dto.ProjectId, new TimeRange(dto.From, dto.To), CatalogEndpoints.ParseCurrency(dto.Currency),
                dto.Slots, dto.CandidateIds, dto.ValiditySeconds), ct);
            response.Headers.CacheControl = "no-store";
            return Results.Created($"/api/v1/planning/quotes/{quote.Id:D}", View(quote));
        });
        routes.MapGet("/{id:guid}", async (Guid id, IPlanningQuoteStore store, HttpResponse response, CancellationToken ct) =>
        {
            var quote = await store.GetAsync(id, ct);
            if (quote is null) return Results.NotFound();
            response.Headers.CacheControl = "no-store";
            return Results.Ok(View(quote));
        });
        return endpoints;
    }
    private static object View(PlanningQuote q) => new { q.Id, q.Digest, q.CreatedBy, q.CreatedAt, q.ExpiresAt, q.ConsumedByWorkflowId, Evidence = q.Evidence };
    public sealed record QuoteDto(Guid ProjectId, DateTimeOffset From, DateTimeOffset To, string Currency,
        IReadOnlyList<DemandSlot> Slots, IReadOnlyList<Guid> CandidateIds, int ValiditySeconds = 300);
}
