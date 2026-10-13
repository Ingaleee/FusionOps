using FusionOps.Application.Abstractions;
using FusionOps.Presentation.Realtime;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Xunit;

namespace FusionOps.Application.Tests;

public sealed class TenantNotificationTests
{
    [Fact]
    public async Task OutboxNotificationsSelectOnlyTheMessageTenantsRoleGroup()
    {
        var tenant = Guid.NewGuid();
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>(MockBehavior.Strict);
        clients.Setup(c => c.Group($"tenant:{tenant:N}:Managers")).Returns(proxy.Object);
        clients.Setup(c => c.Group($"tenant:{tenant:N}:StockAdmins")).Returns(proxy.Object);
        var hub = new Mock<IHubContext<NotificationHub>>(); hub.SetupGet(h => h.Clients).Returns(clients.Object);
        var service = new SignalRNotificationService(hub.Object);
        using (TenantExecution.Enter(TenantScope.For(tenant)))
        {
            await service.AllocationUpdateAsync(Guid.NewGuid(), "reserved", default);
            await service.LowStockAlertAsync("tenant-sku", DateTime.UtcNow, 1, default);
        }
        clients.Verify(c => c.Group($"tenant:{tenant:N}:Managers"), Times.Once);
        clients.Verify(c => c.Group($"tenant:{tenant:N}:StockAdmins"), Times.Once);
        await Assert.ThrowsAsync<FusionOps.Domain.Tenancy.TenantRequiredException>(() => service.AllocationUpdateAsync(Guid.NewGuid(), "reserved", default));
    }
}
