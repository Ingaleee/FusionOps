using FusionOps.Domain.Interfaces;
using MediatR;

namespace FusionOps.Application.UseCases.ForecastStock;

public class ForecastHandler : IRequestHandler<ForecastQuery, IReadOnlyCollection<ForecastDto>>
{
    private readonly IStockRepository _stockRepo;
    private readonly IStockForecaster _forecaster;

    public ForecastHandler(IStockRepository stockRepo, IStockForecaster forecaster)
    {
        _stockRepo = stockRepo;
        _forecaster = forecaster;
    }

    public async Task<IReadOnlyCollection<ForecastDto>> Handle(ForecastQuery request, CancellationToken cancellationToken)
    {
        var lowStock = await _stockRepo.GetLowStockAsync();
        var list = new List<ForecastDto>();

        foreach (var item in lowStock)
        {
            var forecast = await _forecaster.ForecastAsync(item, request.Days);
            var need = (int)MathF.Ceiling(forecast);

            DateTime? shortageDate = null;
            if (need > 0)
            {
                var dailyNeed = forecast / request.Days;
                if (dailyNeed > 0 && item.Quantity < forecast)
                {
                    var daysUntilShortage = item.Quantity / dailyNeed;
                    shortageDate = DateTime.UtcNow.AddDays(daysUntilShortage);
                }
            }

            list.Add(new ForecastDto(item.Sku, need, shortageDate));
        }

        return list;
    }
}