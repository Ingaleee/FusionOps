using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FusionOps.Cli.Commands;

public class StockForecastCommand : Command
{
    private readonly IServiceProvider _serviceProvider;

    public StockForecastCommand(IServiceProvider serviceProvider) : base("forecast", "Get stock forecast for a warehouse")
    {
        _serviceProvider = serviceProvider;

        var warehouseIdOption = new Option<string>("--warehouse-id", "Warehouse ID") { IsRequired = true };
        var daysOption = new Option<int>("--days", "Number of days to forecast") { IsRequired = true };

        AddOption(warehouseIdOption);
        AddOption(daysOption);

        this.SetHandler(async (warehouseId, days) =>
        {
            await HandleForecast(warehouseId, days);
        }, warehouseIdOption, daysOption);
    }

    private async Task HandleForecast(string warehouseId, int days)
    {
        var logger = _serviceProvider.GetRequiredService<ILogger<StockForecastCommand>>();
        var httpClient = _serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();

        try
        {
            var url = $"http://localhost:5000/api/stock/forecast?warehouseId={warehouseId}&days={days}";
            var response = await httpClient.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                var forecast = JsonSerializer.Deserialize<ForecastResult>(result, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                logger.LogInformation("Stock forecast for warehouse {WarehouseId}:", warehouseId);
                logger.LogInformation("Days forecasted: {Days}", days);
                logger.LogInformation("Total items: {TotalItems}", forecast?.TotalItems ?? 0);
                logger.LogInformation("Low stock items: {LowStockItems}", forecast?.LowStockItems ?? 0);
                
                if (forecast?.Items != null)
                {
                    foreach (var item in forecast.Items)
                    {
                        logger.LogInformation("  {Sku}: Current={Current}, Forecast={Forecast}, ReorderPoint={ReorderPoint}", 
                            item.Sku, item.CurrentStock, item.ForecastedStock, item.ReorderPoint);
                    }
                }
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                logger.LogError("Failed to get forecast: {Error}", error);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting forecast");
        }
    }

    private class ForecastResult
    {
        public int TotalItems { get; set; }
        public int LowStockItems { get; set; }
        public List<ForecastItem> Items { get; set; } = new();
    }

    private class ForecastItem
    {
        public string Sku { get; set; } = string.Empty;
        public int CurrentStock { get; set; }
        public int ForecastedStock { get; set; }
        public int ReorderPoint { get; set; }
    }
} 