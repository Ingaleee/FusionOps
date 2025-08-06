using System.CommandLine;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FusionOps.Cli.Commands;

public class NotifyConnectCommand : Command
{
    private readonly IServiceProvider _serviceProvider;

    public NotifyConnectCommand(IServiceProvider serviceProvider) : base("connect", "Connect to SignalR notifications")
    {
        _serviceProvider = serviceProvider;

        var hubUrlOption = new Option<string>("--hub-url", "SignalR hub URL") 
        { 
            IsRequired = false
        };
        hubUrlOption.SetDefaultValue("http://localhost:5000/notificationHub");

        AddOption(hubUrlOption);

        this.SetHandler(async (hubUrl) =>
        {
            await HandleConnect(hubUrl);
        }, hubUrlOption);
    }

    private async Task HandleConnect(string hubUrl)
    {
        var logger = _serviceProvider.GetRequiredService<ILogger<NotifyConnectCommand>>();
        
        try
        {
            logger.LogInformation("Connecting to SignalR hub at: {HubUrl}", hubUrl);

            var connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            // Register handlers for different notification types
            connection.On<string>("AllocationUpdate", (message) =>
            {
                logger.LogInformation("📋 Allocation Update: {Message}", message);
            });

            connection.On<string>("LowStock", (message) =>
            {
                logger.LogWarning("⚠️  Low Stock Alert: {Message}", message);
            });

            connection.On<string>("ResourceAllocated", (message) =>
            {
                logger.LogInformation("✅ Resource Allocated: {Message}", message);
            });

            connection.On<string>("StockReplenished", (message) =>
            {
                logger.LogInformation("📦 Stock Replenished: {Message}", message);
            });

            // Handle connection events
            connection.Closed += async (error) =>
            {
                logger.LogWarning("Connection closed: {Error}", error?.Message);
                await Task.Delay(5000); // Wait before reconnecting
            };

            connection.Reconnecting += (error) =>
            {
                logger.LogInformation("Reconnecting... {Error}", error?.Message);
                return Task.CompletedTask;
            };

            connection.Reconnected += (connectionId) =>
            {
                logger.LogInformation("Reconnected with connection ID: {ConnectionId}", connectionId);
                return Task.CompletedTask;
            };

            // Start the connection
            await connection.StartAsync();
            logger.LogInformation("✅ Connected to SignalR hub. Listening for notifications...");
            logger.LogInformation("Press Ctrl+C to disconnect");

            // Keep the connection alive
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Disconnecting...");
            }

            await connection.StopAsync();
            logger.LogInformation("Disconnected from SignalR hub");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error connecting to SignalR hub");
        }
    }
} 