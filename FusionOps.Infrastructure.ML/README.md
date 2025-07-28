# Stock Forecast ML Model

## Как обучить модель

1. Установи Jupyter с .NET kernel (dotnet-interactive).
2. Открой `GenerateModel.ipynb`.
3. Запусти все ячейки — модель обучится на `../../docker/sqlserver/init/stock_history.csv` и сохранится в `Artifacts/model.zip`.

## Как использовать в коде

- Для инференса из C# используй:

```csharp
var forecast = StockForecastModel.Predict(historyArray);
```

- В проде модель подхватывается через DI в `MlNetOptimizer`. 