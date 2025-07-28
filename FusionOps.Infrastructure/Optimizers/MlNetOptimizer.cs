using FusionOps.Domain.Entities;
using FusionOps.Domain.Interfaces;
using Microsoft.ML;
using System.IO;
using Microsoft.ML.Data;

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
    private readonly MLContext _mlContext = new();
    private readonly ITransformer _model;
    private readonly PredictionEngine<StockHistoryInput, StockForecastOutput> _engine;

    public MlNetOptimizer()
    {
        var modelPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "FusionOps.Infrastructure.ML", "Artifacts", "model.zip");
        _model = _mlContext.Model.Load(modelPath, out var schema);
        _engine = _mlContext.Model.CreatePredictionEngine<StockHistoryInput, StockForecastOutput>(_model);
    }

    public Task<float> ForecastAsync(StockItem item, int days)
    {
        // TODO: получить историю продаж item (QtySold[]), здесь для примера - последние 30 дней по 10
        var input = new StockHistoryInput { History = Enumerable.Repeat(10f, 30).ToArray() };
        var output = _engine.Predict(input);
        return Task.FromResult(output.ForecastedQty.FirstOrDefault());
    }
}

public class StockHistoryInput
{
    [VectorType(30)]
    public float[] History { get; set; } = Array.Empty<float>();
}

public class StockForecastOutput
{
    [VectorType(7)]
    public float[] ForecastedQty { get; set; } = Array.Empty<float>();
}