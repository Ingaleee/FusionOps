using System.CommandLine;

namespace FusionOps.Cli.Commands;

public class NotifyCommand : Command
{
    public NotifyCommand(IServiceProvider serviceProvider) : base("notify", "Real-time notification operations")
    {
        AddCommand(new NotifyConnectCommand(serviceProvider));
    }
} 