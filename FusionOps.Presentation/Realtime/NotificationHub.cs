using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace FusionOps.Presentation.Realtime;

[Authorize]
public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user?.IsInRole("Stock.Admin") == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, Group("StockAdmins"));
        if (user?.IsInRole("Resource.Manager") == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, Group("Managers"));
        await base.OnConnectedAsync();
    }

    [Authorize(Roles = "Stock.Admin")]
    public async Task SendLowStockAlert(string sku, DateTime expectedDate, int qtyLeft)
        => await Clients.Group(Group("StockAdmins")).SendAsync("lowStock", new { sku, expectedDate, qtyLeft });

    [Authorize(Roles = "Resource.Manager")]
    public async Task SendAllocationUpdate(Guid projectId, string status)
        => await Clients.Group(Group("Managers")).SendAsync("allocationUpdate", new { projectId, status });

    private string Group(string role)
    {
        var claim = Context.User?.FindAll("tenant_id").ToArray();
        if (claim is not { Length: 1 } || !Guid.TryParseExact(claim[0].Value, "D", out var tenant) || tenant == Guid.Empty)
            throw new HubException("A verified tenant claim is required");
        return $"tenant:{tenant:N}:{role}";
    }
}
