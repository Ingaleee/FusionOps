using System.CommandLine;
using FusionOps.Cli.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddHttpClient();

var serviceProvider = services.BuildServiceProvider();

var rootCommand = new RootCommand("FusionOps CLI - Resource Management Tool");

// Add subcommands
rootCommand.AddCommand(new AllocateCommand(serviceProvider));
rootCommand.AddCommand(new StockCommand(serviceProvider));
rootCommand.AddCommand(new NotifyCommand(serviceProvider));

return await rootCommand.InvokeAsync(args); 