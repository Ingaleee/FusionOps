using FusionOps.Domain.Entities;
using FusionOps.Domain.Interfaces;
using Microsoft.ML;

namespace FusionOps.Infrastructure.Optimizers;

public class StockDemandInput
{
    public float[] History { get; set; } = Array.Empty<float>();
}

public class StockDemandOutput
{
    public float Forecast { get; set; }
}

/// <summary>
/// Заглушка ML-прогнозировщика на базе Microsoft.ML. Пока возвращает случайное значение 80-120% от текущего запаса.
/// </summary>
public sealed class MlNetOptimizer : IStockForecaster
{
    private readonly Random _rnd = new();
    private readonly MLContext _mlContext = new();

    public Task<float> ForecastAsync(StockItem item, int days)
    {
        var factor = _rnd.Next(80, 121) / 100f;
        var forecast = item.Quantity * factor;
        return Task.FromResult(forecast);
    }
} 