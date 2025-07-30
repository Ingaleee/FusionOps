using Microsoft.AspNetCore.SignalR;

namespace FusionOps.Presentation.Realtime;

public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user?.IsInRole("Stock.Admin") == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, "StockAdmins");
        if (user?.IsInRole("Resource.Manager") == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, "Managers");
        await base.OnConnectedAsync();
    }

    public async Task SendLowStockAlert(string sku, DateTime expectedDate, int qtyLeft)
        => await Clients.Group("StockAdmins").SendAsync("lowStock", new { sku, expectedDate, qtyLeft });

    public async Task SendAllocationUpdate(Guid projectId, string status)
        => await Clients.Group("Managers").SendAsync("allocationUpdate", new { projectId, status });
} 