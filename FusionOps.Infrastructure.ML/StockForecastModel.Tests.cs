using Xunit;

public class StockForecastModelTests
{
    [Fact]
    public void Predict_ReturnsReasonableValue()
    {
        // Fake history: последние 30 дней по 10
        var history = Enumerable.Repeat(10f, 30).ToArray();
        var forecast = StockForecastModel.Predict(history);
        Assert.InRange(forecast, 8, 12); // Ожидаем, что прогноз близок к последней точке
    }
} 