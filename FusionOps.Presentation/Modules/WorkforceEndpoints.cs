using FusionOps.Application.UseCases.AllocateResource;
using FusionOps.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace FusionOps.Presentation.Modules;

public static class WorkforceEndpoints
{
    public static IEndpointRouteBuilder MapWorkforceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var allocate = endpoints.MapPost("/api/v1/allocate", async (AllocateCommandDTO dto, HttpRequest request, ISender sender,
            FusionOps.Application.Projects.IProjectAccess access, CancellationToken ct) =>
        {
            await access.RequireAsync(dto.ProjectId, FusionOps.Domain.Projects.ProjectPermission.Execute, ct);
            var key = request.Headers.TryGetValue("Idempotency-Key", out var keys) ? keys.ToString() : null;
            var cmd = new AllocateCommand(dto.ProjectId, dto.ResourceIds, dto.PeriodFrom, dto.PeriodTo, key);
            var ids = await sender.Send(cmd, ct);
            return Results.Created($"/api/v1/projects/allocations?ids={dto.ProjectId:D}", ids);
        });

        // В dev-окружении разрешаем сидинг без авторизации
        var env = endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (!env.IsDevelopment())
        {
            allocate.RequireAuthorization("ManageResources");
        }

        return endpoints;
    }

    public record AllocateCommandDTO(Guid ProjectId, IReadOnlyCollection<Guid> ResourceIds, DateTimeOffset PeriodFrom, DateTimeOffset PeriodTo);
}
