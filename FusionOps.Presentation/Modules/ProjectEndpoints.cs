using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MediatR;
using FusionOps.Application.Dto;
using FusionOps.Application.Queries;

namespace FusionOps.Presentation.Modules;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var endpoint = app.MapGet("/api/v1/projects/allocations", async (string ids, ISender sender, FusionOps.Application.Projects.IProjectAccess access, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(ids)) return Results.BadRequest("ids required");
            var parsed = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(s => Guid.TryParse(s, out var g) ? g : (Guid?)null)
                            .Where(g => g.HasValue)
                            .Select(g => g!.Value)
                            .Distinct()
                            .ToArray();
            if (parsed.Length == 0) return Results.BadRequest("no valid ids");
            if (parsed.Length > 100) return Results.BadRequest("at most 100 project ids are supported");
            foreach (var projectId in parsed) await access.RequireAsync(projectId, FusionOps.Domain.Projects.ProjectPermission.Read, ct);

            var result = await sender.Send(new GetAllocationsBatchedQuery(parsed), ct);
            return Results.Ok(result);
        })
        .WithName("GetAllocationsBatched")
        .Produces<IDictionary<Guid, IEnumerable<AllocationDto>>>(StatusCodes.Status200OK);
        if (!app.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment()) endpoint.RequireAuthorization("ManageResources");

        return app;
    }
}
