using FusionOps.Domain.Events;
using FusionOps.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FusionOps.Application.Notifications;

public class ResourceAllocatedHandler : INotificationHandler<ResourceAllocated>
{
    private readonly ITelemetryProducer _telemetry;
    private readonly ILogger<ResourceAllocatedHandler> _logger;

    public ResourceAllocatedHandler(ITelemetryProducer telemetry, ILogger<ResourceAllocatedHandler> logger)
    {
        _telemetry = telemetry;
        _logger = logger;
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
    }
} 