using FusionOps.Application.UseCases.ForecastStock;
using MediatR;

namespace FusionOps.Presentation.Modules;

public static class StockEndpoints
{
    public static IEndpointRouteBuilder MapStockEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/stock/forecast", async (int days, ISender sender) =>
        {
            if (days <= 0) days = 30;
            var result = await sender.Send(new ForecastQuery(days));
            return Results.Ok(result);
        });

        return endpoints;
    }
} 