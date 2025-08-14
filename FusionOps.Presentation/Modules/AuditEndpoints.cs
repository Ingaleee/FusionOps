using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using FusionOps.Application.Queries;
using FusionOps.Application.Dto;

namespace FusionOps.Presentation.Modules;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/audit/allocations/{projectId:guid}",
            [Authorize(Policy = "Audit.Read")]
            async (Guid projectId,
                   int? page,
                   int? pageSize,
                   DateTime? from,
                   DateTime? to,
                   ISender sender) =>
            {
                var query = new GetAllocationHistoryQuery(projectId, from, to,
                              page ?? 1, pageSize ?? 100);
                var result = await sender.Send(query);
                return Results.Ok(result);
            })
            .WithName("GetAllocationHistory")
            .Produces<PagedResult<AllocationHistoryDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
    }
}
