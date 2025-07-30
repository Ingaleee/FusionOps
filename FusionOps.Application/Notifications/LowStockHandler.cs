using MediatR;
using FusionOps.Domain.Events;
using FusionOps.Application.Abstractions;

namespace FusionOps.Application.Notifications;

public sealed class LowStockHandler : INotificationHandler<ReorderPointReached>
{
    private readonly IResourceNotification _notify;
    public LowStockHandler(IResourceNotification n) => _notify = n;

    public Task Handle(ReorderPointReached e, CancellationToken ct) =>
        _notify.LowStockAlertAsync(e.Sku, DateTime.UtcNow.AddDays(3), e.QuantityLeft, ct);
} 