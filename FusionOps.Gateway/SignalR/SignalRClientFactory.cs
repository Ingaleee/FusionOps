using Microsoft.AspNetCore.SignalR.Client;
using HotChocolate.Subscriptions;
using FusionOps.Gateway.Models;

namespace FusionOps.Gateway.SignalR;

public sealed class SignalRClientFactory : IHostedService
{
    private readonly ITopicEventSender _sender;
    private readonly IConfiguration _cfg;
    private HubConnection? _conn;

    public SignalRClientFactory(ITopicEventSender s, IConfiguration cfg) => (_sender, _cfg) = (s, cfg);

    public async Task StartAsync(CancellationToken ct)
    {
        _conn = new HubConnectionBuilder()
            .WithUrl(_cfg["FusionApi:SignalR"] ?? "http://localhost:5000/hubs/notify")
            .WithAutomaticReconnect()
            .Build();

        _conn.On<LowStockAlert>("lowStock",
            async alert => await _sender.SendAsync(nameof(GraphQL.Subscription.LowStock), alert, ct));

        await _conn.StartAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => _conn?.DisposeAsync().AsTask() ?? Task.CompletedTask;
}
