using FusionOps.Application.UseCases.License;
using MediatR;
using Microsoft.AspNetCore.Authorization;

namespace FusionOps.Presentation.Modules;

public static class LicenseEndpoints
{
    public static IEndpointRouteBuilder MapLicenseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/licenses/{product}/allocate", [Authorize(Roles = "License.Manager")] async (string product, int seats, Guid projectId, ISender sender) =>
        {
            await sender.Send(new AllocateLicenseCommand(product, projectId, seats));
            return Results.Accepted();
        });

        endpoints.MapPost("/api/v1/licenses/{product}/release", [Authorize(Roles = "License.Manager")] async (string product, int seats, Guid projectId, ISender sender) =>
        {
            await sender.Send(new ReleaseLicenseCommand(product, projectId, seats));
            return Results.Accepted();
        });

        return endpoints;
    }
}


