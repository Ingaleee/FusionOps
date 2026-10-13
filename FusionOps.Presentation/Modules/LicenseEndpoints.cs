using FusionOps.Application.UseCases.License;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Entities;
using FusionOps.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Presentation.Modules;

public static class LicenseEndpoints
{
    public static IEndpointRouteBuilder MapLicenseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Admin: create or update a license pool (dev: allow anonymous)
        var env = endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var createPool = endpoints.MapPost("/api/v1/licenses/{product}/pool", async (string product, int totalSeats, ISender sender, CancellationToken ct) =>
        {
            var id = await sender.Send(new ConfigureLicensePoolCommand(product, totalSeats), ct);
            return Results.Ok(new { id, product, totalSeats });
        });
        if (env.IsDevelopment())
            createPool.WithMetadata(new AllowAnonymousAttribute());
        else
            createPool.RequireAuthorization("License.Manager");

        var allocate = endpoints.MapPost("/api/v1/licenses/{product}/allocate", [Authorize(Roles = "License.Manager")] async (string product, int seats, Guid projectId, ISender sender,
            FusionOps.Application.Projects.IProjectAccess access, CancellationToken ct) =>
        {
            await access.RequireAsync(projectId, FusionOps.Domain.Projects.ProjectPermission.Execute, ct);
            await sender.Send(new AllocateLicenseCommand(product, projectId, seats), ct);
            return Results.Accepted();
        });
        if (env.IsDevelopment()) allocate.WithMetadata(new AllowAnonymousAttribute());

        var release = endpoints.MapPost("/api/v1/licenses/{product}/release", [Authorize(Roles = "License.Manager")] async (string product, int seats, Guid projectId, ISender sender,
            FusionOps.Application.Projects.IProjectAccess access, CancellationToken ct) =>
        {
            await access.RequireAsync(projectId, FusionOps.Domain.Projects.ProjectPermission.Execute, ct);
            await sender.Send(new ReleaseLicenseCommand(product, projectId, seats), ct);
            return Results.Accepted();
        });
        if (env.IsDevelopment()) release.WithMetadata(new AllowAnonymousAttribute());

        // Read endpoints
        var list = endpoints.MapGet("/api/v1/licenses", [Authorize(Roles = "License.Manager")] async (FulfillmentContext ctx) =>
        {
            var pools = await ctx.Set<LicensePool>().Select(p => new { product = p.Product, p.TotalSeats, p.AllocatedSeats }).ToListAsync();
            return Results.Ok(pools);
        });
        if (env.IsDevelopment()) list.WithMetadata(new AllowAnonymousAttribute());

        var detail = endpoints.MapGet("/api/v1/licenses/{product}", [Authorize(Roles = "License.Manager")] async (string product, ILicenseRepository repo) =>
        {
            var pool = await repo.FindByProductAsync(product);
            return pool is null ? Results.NotFound() : Results.Ok(new { product = pool.Product, pool.TotalSeats, pool.AllocatedSeats, pool.TemporalReservedPeak });
        });
        if (env.IsDevelopment()) detail.WithMetadata(new AllowAnonymousAttribute());

        return endpoints;
    }
}


