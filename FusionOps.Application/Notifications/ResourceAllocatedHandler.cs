using FusionOps.Domain.Events;
using FusionOps.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using FusionOps.Application.Abstractions;

namespace FusionOps.Application.Notifications;

public class ResourceAllocatedHandler : INotificationHandler<ResourceAllocated>
{
    private readonly ITelemetryProducer _telemetry;
    private readonly ILogger<ResourceAllocatedHandler> _logger;
    private readonly IResourceNotification _notify;

    public ResourceAllocatedHandler(ITelemetryProducer telemetry, ILogger<ResourceAllocatedHandler> logger, IResourceNotification notify)
    {
        _telemetry = telemetry;
        _logger = logger;
        _notify = notify;
    }

    public async Task Handle(ResourceAllocated notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Sending telemetry for ResourceAllocated {Id}", notification.AllocationId);
        await _telemetry.PublishAsync("resource_allocated", new
        {
            notification.AllocationId,
            notification.ResourceId,
            notification.ProjectId,
            notification.Period
        });
        await _notify.AllocationUpdateAsync(notification.ProjectId, "RESERVED", cancellationToken);
    }
}