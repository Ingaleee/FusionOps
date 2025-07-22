using FusionOps.Application.UseCases.AllocateResource;
using FusionOps.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace FusionOps.Presentation.Modules;

public static class WorkforceEndpoints
{
    public static IEndpointRouteBuilder MapWorkforceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/allocate", async (AllocateCommandDTO dto, ISender sender) =>
        {
            var cmd = new AllocateCommand(dto.ProjectId, dto.ResourceIds, dto.PeriodFrom, dto.PeriodTo);
            try
            {
                var ids = await sender.Send(cmd);
                return Results.Created("/api/v1/allocations", ids);
            }
            catch (DomainException ex)
            {
                return Results.UnprocessableEntity(new { error = ex.Message });
            }
        });

        return endpoints;
    }

    public record AllocateCommandDTO(Guid ProjectId, IReadOnlyCollection<Guid> ResourceIds, DateTime PeriodFrom, DateTime PeriodTo);
} 