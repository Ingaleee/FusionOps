using Microsoft.ML;
using Microsoft.ML.Data;

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

public static class StockForecastModel
{
    public static float Predict(float[] history)
    {
        var mlContext = new MLContext();
        var model = mlContext.Model.Load("Artifacts/model.zip", out var schema);
        var engine = mlContext.Model.CreatePredictionEngine<StockHistoryInput, StockForecastOutput>(model);
        var input = new StockHistoryInput { History = history };
        var output = engine.Predict(input);
        return output.ForecastedQty.FirstOrDefault();
    }
} 