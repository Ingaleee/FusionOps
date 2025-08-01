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
            .WithOpenApi(operation =>
            {
                operation.Summary = "Get allocation history for a project";
                operation.Description = "Retrieves paginated allocation history with optional date filtering. " +
                                     "Requires Audit.Read permission. Supports pagination and date range filtering.";
                operation.Parameters[0].Description = "Project identifier";
                operation.Parameters[1].Description = "Page number (default: 1)";
                operation.Parameters[2].Description = "Page size (10-500, default: 100)";
                operation.Parameters[3].Description = "Filter from date (optional)";
                operation.Parameters[4].Description = "Filter to date (optional)";
                return operation;
            })
            .Produces<PagedResult<AllocationHistoryDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
    }
}
