using FusionOps.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Presentation.Modules;

public static class TenantOpsEndpoints
{
    public static IEndpointRouteBuilder MapTenantOpsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/ops/tenants");

        group.MapPost("/{slug}", async (string slug, FulfillmentContext ctx) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9-]{3,32}$"))
                return Results.BadRequest("invalid slug");
            var schema = $"t_{slug}";
            await ctx.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{schema}\"");
            return Results.Ok(new { schema });
        }).RequireAuthorization("T:AdminStock");

        return app;
    }
}


