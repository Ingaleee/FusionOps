using System.Globalization;
using FusionOps.Application.Execution;
using FusionOps.Domain.Execution;
namespace FusionOps.Presentation.Modules;
public static class ExecutionTaskEndpoints
{
    public static IEndpointRouteBuilder MapExecutionTaskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/projects/{projectId:guid}/execution-plans/{id:guid}/tasks").WithTags("Execution tasks");
        var development = endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment(); if (!development) group.RequireAuthorization();
        group.MapGet("/", async (Guid projectId, Guid id, IExecutionTaskStore store, HttpResponse response, CancellationToken ct) =>
        { response.Headers.CacheControl = "no-store"; var tasks = await store.ListAsync(projectId, id, ct); return tasks is null ? Results.NotFound() : Results.Ok(tasks); });
        foreach (var mutation in Enum.GetValues<ExecutionTaskMutation>())
        {
            var route = group.MapPost($"/{{taskId:guid}}/{mutation.ToString().ToLowerInvariant()}", async (Guid projectId, Guid id, Guid taskId, ExecutionTaskCommand command, HttpRequest request, HttpResponse response, IExecutionTaskStore store, CancellationToken ct) =>
            {
                if (!request.Headers.TryGetValue("If-Match", out var values)) throw new ExecutionPlanPreconditionRequiredException(); var value = values.ToString();
                if (values.Count != 1 || value.Length < 3 || value[0] != '"' || value[^1] != '"'
                    || !long.TryParse(value.AsSpan(1, value.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1) throw new ArgumentException("Supply one quoted positive task ETag");
                var fact = await store.ExecuteAsync(projectId, id, taskId, version, mutation, command, ct);
                response.Headers.CacheControl = "no-store";
                return Results.Json(fact, statusCode: fact.Accepted ? 200 : 422);
            });
            if (!development) route.RequireAuthorization("ManageResources");
        }
        return endpoints;
    }
}
