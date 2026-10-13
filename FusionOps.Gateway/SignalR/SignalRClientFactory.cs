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
        if (!Guid.TryParseExact(_cfg["FusionApi:SubscriptionTenant"], "D", out var tenant) || tenant == Guid.Empty
            || string.IsNullOrWhiteSpace(_cfg["FusionApi:SubscriptionToken"]))
            throw new InvalidOperationException("A tenant-bound subscription bridge requires a verified service token and tenant ID");
        var bridgeToken = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(_cfg["FusionApi:SubscriptionToken"]);
        var tenantClaims = bridgeToken.Claims.Where(c => c.Type == "tenant_id").ToArray();
        if (tenantClaims.Length != 1 || !Guid.TryParseExact(tenantClaims[0].Value, "D", out var claimedTenant) || claimedTenant != tenant)
            throw new InvalidOperationException("The bridge topic must match the service token tenant; the API verifies the token signature");
        _conn = new HubConnectionBuilder()
            .WithUrl(_cfg["FusionApi:SignalR"] ?? "http://localhost:5000/hubs/notify", options => options.AccessTokenProvider = () => Task.FromResult(_cfg["FusionApi:SubscriptionToken"]))
            .WithAutomaticReconnect()
            .Build();

        _conn.On<LowStockAlert>("lowStock",
            async alert => await _sender.SendAsync($"tenant:{tenant:N}:lowStock", alert, ct));

        await _conn.StartAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => _conn?.DisposeAsync().AsTask() ?? Task.CompletedTask;
}
