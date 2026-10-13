using System.Globalization;
using FusionOps.Application.Execution;
using FusionOps.Domain.Execution;

namespace FusionOps.Presentation.Modules;

public static class ExecutionPlanEndpoints
{
    public static IEndpointRouteBuilder MapExecutionPlanEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/api/v1/projects/{projectId:guid}/execution-plans").WithTags("Execution plan authority");
        var development = endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment();
        if (!development) routes.RequireAuthorization();
        var create = routes.MapPost("/", async (Guid projectId, ExecutionPlanRequest request, IExecutionPlanStore store, HttpResponse response, CancellationToken ct) =>
        {
            var plan = await store.CreateAsync(projectId, request, ct); Headers(response, plan);
            return Results.Created($"/api/v1/projects/{projectId:D}/execution-plans/{plan.Id:D}", View(plan));
        });
        if (!development) create.RequireAuthorization("ManageResources");
        routes.MapGet("/", async (Guid projectId, Guid? after, int? limit, IExecutionPlanStore store, HttpResponse response, CancellationToken ct) =>
        { response.Headers.CacheControl = "no-store"; return Results.Ok(await store.ListAsync(projectId, after, limit ?? 20, ct)); });
        routes.MapGet("/{id:guid}", async (Guid projectId, Guid id, IExecutionPlanStore store, HttpResponse response, CancellationToken ct) =>
        {
            var details = await store.GetAsync(projectId, id, ct); if (details is null) return Results.NotFound(); Headers(response, details.Plan);
            return Results.Ok(new { Plan = View(details.Plan), details.Decisions });
        });
        foreach (var mutation in Enum.GetValues<ExecutionPlanMutation>())
        {
            var route = routes.MapPost($"/{{id:guid}}/{mutation.ToString().ToLowerInvariant()}", async (Guid projectId, Guid id,
                ExecutionDecisionDto dto, HttpRequest request, HttpResponse response, IExecutionPlanStore store, CancellationToken ct) =>
            {
                var plan = await store.MutateAsync(projectId, id, Version(request), mutation, dto.ReviewedDigest, dto.Reason, ct);
                if (plan is null) return Results.NotFound(); Headers(response, plan); return Results.Ok(View(plan));
            });
            if (!development) route.RequireAuthorization("ManageResources");
        }
        return endpoints;
    }
    private static object View(ExecutionPlan p) => new { p.Id, p.ProjectId, p.Version, p.State, p.ApprovedBy, p.ApprovedAt, p.ApprovalExpiresAt,
        p.UpdatedAt, p.Baseline.Digest, p.Baseline.ScenarioId, p.Baseline.PreviousPlanId, p.Baseline.ProjectVersion, p.Baseline.DefinitionVersion,
        p.Baseline.DefinitionDigest, p.Baseline.CreatedBy, p.Baseline.CreatedAt, p.Baseline.ReviewExpiresAt, p.Baseline.TotalCost,
        p.Baseline.Currency, p.Baseline.Evidence };
    private static void Headers(HttpResponse response, ExecutionPlan plan)
    { response.Headers.ETag = $"\"{plan.Version.ToString(CultureInfo.InvariantCulture)}\""; response.Headers.CacheControl = "no-store"; }
    private static long Version(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("If-Match", out var values)) throw new ExecutionPlanPreconditionRequiredException();
        var text = values.ToString();
        if (values.Count != 1 || text.Length < 3 || text[0] != '"' || text[^1] != '"'
            || !long.TryParse(text.AsSpan(1, text.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
            throw new ArgumentException("If-Match must contain one quoted positive execution plan version");
        return version;
    }
    public sealed record ExecutionDecisionDto(string ReviewedDigest, string? Reason = null);
}
public sealed class ExecutionPlanPreconditionRequiredException : Exception;
