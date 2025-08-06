using System.CommandLine;

namespace FusionOps.Cli.Commands;

public class StockCommand : Command
{
    public StockCommand(IServiceProvider serviceProvider) : base("stock", "Stock management operations")
    {
        AddCommand(new StockForecastCommand(serviceProvider));
    }
} 