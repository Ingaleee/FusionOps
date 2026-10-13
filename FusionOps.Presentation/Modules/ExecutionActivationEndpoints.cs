using System.Globalization;
using FusionOps.Application.Execution;
using FusionOps.Domain.Execution;

namespace FusionOps.Presentation.Modules;

public static class ExecutionActivationEndpoints
{
    public static IEndpointRouteBuilder MapExecutionActivationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/api/v1/projects/{projectId:guid}/execution-plans/{id:guid}/activation").WithTags("Execution activation");
        var development = endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment();
        if (!development) routes.RequireAuthorization();
        var activate = routes.MapPost("/", async (Guid projectId, Guid id, ActivateDto dto, HttpRequest request, HttpResponse response, IExecutionActivationStore store, CancellationToken ct) =>
        {
            var activation = await store.RequestAsync(projectId, id, Version(request), dto.ReviewedDigest, ct); Headers(response, activation);
            return Results.Accepted($"/api/v1/projects/{projectId:D}/execution-plans/{id:D}/activation", View(activation));
        });
        if (!development) activate.RequireAuthorization("ManageResources");
        routes.MapGet("/", async (Guid projectId, Guid id, HttpResponse response, IExecutionActivationStore store, CancellationToken ct) =>
        {
            var details = await store.GetAsync(projectId, id, ct); if (details is null) return Results.NotFound(); Headers(response, details.Activation);
            return Results.Ok(new { Activation = View(details.Activation), details.Decisions });
        });
        foreach (var mutation in Enum.GetValues<ActivationMutation>())
        {
            var route = routes.MapPost($"/{mutation.ToString().ToLowerInvariant()}", async (Guid projectId, Guid id, ActivationMutationDto dto, HttpRequest request, HttpResponse response, IExecutionActivationStore store, CancellationToken ct) =>
            {
                var activation = await store.MutateAsync(projectId, id, Version(request), mutation, dto.Reason, ct);
                if (activation is null) return Results.NotFound(); Headers(response, activation); return Results.Ok(View(activation));
            });
            if (!development) route.RequireAuthorization("ManageResources");
        }
        return endpoints;
    }
    private static object View(ExecutionActivation a) => new { a.Id, a.ProjectId, a.Digest, a.ApprovedPlanVersion, a.RequestedBy, a.Version, a.State,
        a.CancellationRequested, a.WorkCompletionRequested, a.FailureCode, a.LastErrorCode, a.PausedStep, a.StepAttempts, a.NextAttemptAt, a.CreatedAt, a.UpdatedAt, a.ResourceOwnership };
    private static void Headers(HttpResponse response, ExecutionActivation a)
    { response.Headers.ETag = $"\"{a.Version.ToString(CultureInfo.InvariantCulture)}\""; response.Headers.CacheControl = "no-store"; }
    private static long Version(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("If-Match", out var values)) throw new ExecutionPlanPreconditionRequiredException();
        var value = values.ToString();
        if (values.Count != 1 || value.Length < 3 || value[0] != '"' || value[^1] != '"'
            || !long.TryParse(value.AsSpan(1, value.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
            throw new ArgumentException("If-Match must contain one quoted positive plan/activation version");
        return version;
    }
    public sealed record ActivateDto(string ReviewedDigest);
    public sealed record ActivationMutationDto(string? Reason = null);
}
