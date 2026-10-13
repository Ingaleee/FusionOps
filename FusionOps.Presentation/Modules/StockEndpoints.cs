using FusionOps.Application.UseCases.ForecastStock;
using MediatR;

namespace FusionOps.Presentation.Modules;

public static class StockEndpoints
{
    public static IEndpointRouteBuilder MapStockEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var endpoint = endpoints.MapGet("/api/v1/stock/forecast", async (int days, ISender sender, CancellationToken ct) =>
        {
            if (days <= 0) days = 30;
            if (days > 366) return Results.BadRequest("forecast horizon cannot exceed 366 days");
            var result = await sender.Send(new ForecastQuery(days), ct);
            return Results.Ok(result);
        });
        if (!endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment()) endpoint.RequireAuthorization("AdminStock");

        return endpoints;
    }
}
