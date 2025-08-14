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
        var createPool = endpoints.MapPost("/api/v1/licenses/{product}/pool", async (string product, int totalSeats, ILicenseRepository repo) =>
        {
            var pool = await repo.FindByProductAsync(product) ?? new LicensePool(product, 0);
            if (pool.TotalSeats < totalSeats)
            {
                pool.IncreaseCapacity(totalSeats - pool.TotalSeats);
            }
            if (pool.Id == default)
            {
                await repo.AddAsync(pool);
            }
            else
            {
                await repo.UpdateAsync(pool);
            }
            return Results.Ok(new { product = pool.Product, pool.TotalSeats, pool.AllocatedSeats });
        });
        if (env.IsDevelopment())
            createPool.WithMetadata(new AllowAnonymousAttribute());
        else
            createPool.RequireAuthorization("License.Manager");

        var allocate = endpoints.MapPost("/api/v1/licenses/{product}/allocate", [Authorize(Roles = "License.Manager")] async (string product, int seats, Guid projectId, ISender sender) =>
        {
            await sender.Send(new AllocateLicenseCommand(product, projectId, seats));
            return Results.Accepted();
        });
        if (env.IsDevelopment()) allocate.WithMetadata(new AllowAnonymousAttribute());

        var release = endpoints.MapPost("/api/v1/licenses/{product}/release", [Authorize(Roles = "License.Manager")] async (string product, int seats, Guid projectId, ISender sender) =>
        {
            await sender.Send(new ReleaseLicenseCommand(product, projectId, seats));
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

        endpoints.MapGet("/api/v1/licenses/{product}", [Authorize(Roles = "License.Manager")] async (string product, ILicenseRepository repo) =>
        {
            var pool = await repo.FindByProductAsync(product);
            return pool is null ? Results.NotFound() : Results.Ok(new { product = pool.Product, pool.TotalSeats, pool.AllocatedSeats });
        }).WithMetadata(new AllowAnonymousAttribute());

        return endpoints;
    }
}


