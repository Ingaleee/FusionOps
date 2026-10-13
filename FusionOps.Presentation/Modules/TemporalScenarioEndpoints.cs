using FusionOps.Application.Scheduling;
using FusionOps.Domain.Scheduling;

namespace FusionOps.Presentation.Modules;

public static class TemporalScenarioEndpoints
{
    public static IEndpointRouteBuilder MapTemporalScenarioEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/api/v1/projects/{projectId:guid}/scenarios").WithTags("Temporal project planning");
        var development = endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment();
        if (!development) routes.RequireAuthorization();
        var calculate = routes.MapPost("/", async (Guid projectId, TemporalScenarioRequest request, ITemporalScenarioStore store, HttpResponse response, CancellationToken ct) =>
        {
            var scenario = await store.CreateAsync(projectId, request, ct); response.Headers.CacheControl = "no-store";
            response.Headers.ETag = $"\"{scenario.Digest}\"";
            return Results.Created($"/api/v1/projects/{projectId:D}/scenarios/{scenario.Id:D}", View(scenario));
        });
        if (!development) calculate.RequireAuthorization("ManageResources");
        routes.MapGet("/", async (Guid projectId, Guid? after, int? limit, ITemporalScenarioStore store, HttpResponse response, CancellationToken ct) =>
        { response.Headers.CacheControl = "no-store"; return Results.Ok(await store.ListAsync(projectId, after, limit ?? 20, ct)); });
        routes.MapGet("/{id:guid}", async (Guid projectId, Guid id, ITemporalScenarioStore store, HttpResponse response, CancellationToken ct) =>
        {
            var scenario = await store.GetAsync(projectId, id, ct); if (scenario is null) return Results.NotFound();
            response.Headers.CacheControl = "no-store"; response.Headers.ETag = $"\"{scenario.Digest}\""; return Results.Ok(View(scenario));
        });
        var reproduce = routes.MapPost("/{id:guid}/replay", async (Guid projectId, Guid id, ITemporalScenarioStore store, HttpResponse response, CancellationToken ct) =>
        { response.Headers.CacheControl = "no-store"; var replay = await store.ReplayAsync(projectId, id, ct); return replay is null ? Results.NotFound() : Results.Ok(replay); });
        if (!development) reproduce.RequireAuthorization("ManageResources");
        routes.MapGet("/compare", async (Guid projectId, Guid left, Guid right, ITemporalScenarioStore store, HttpResponse response, CancellationToken ct) =>
        { response.Headers.CacheControl = "no-store"; var comparison = await store.CompareAsync(projectId, left, right, ct); return comparison is null ? Results.NotFound() : Results.Ok(comparison); });
        return endpoints;
    }
    private static object View(TemporalScenario s) => new { s.Id, s.ProjectId, s.ProjectVersion, s.DefinitionVersion, s.Actor, s.CreatedAt,
        s.Digest, s.IsComplete, s.SearchComplete, s.IsHypothetical, s.TotalCost, s.FinishAt, s.Evidence };
}
