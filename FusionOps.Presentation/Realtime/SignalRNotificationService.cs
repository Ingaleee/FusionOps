using FusionOps.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace FusionOps.Presentation.Realtime;

public sealed class SignalRNotificationService : IResourceNotification
{
    private readonly IHubContext<NotificationHub> _hub;

    public SignalRNotificationService(IHubContext<NotificationHub> hub) => _hub = hub;

    public Task LowStockAlertAsync(string sku, DateTime expected, int qty, CancellationToken ct)
        => _hub.Clients.Group("StockAdmins")
              .SendAsync("lowStock", new { sku, expectedDate = expected, qtyLeft = qty }, ct);

    public Task AllocationUpdateAsync(Guid projectId, string status, CancellationToken ct)
        => _hub.Clients.Group("Managers")
              .SendAsync("allocationUpdate", new { projectId, status }, ct);
} 