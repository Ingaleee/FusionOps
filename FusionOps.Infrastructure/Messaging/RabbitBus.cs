using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Shared.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace FusionOps.Infrastructure.Messaging;

public class RabbitBus : IEventBus
{
    private readonly IPublishEndpoint _publish;
    private readonly ILogger<RabbitBus> _logger;

    public RabbitBus(IPublishEndpoint publish, ILogger<RabbitBus> logger)
    {
        _publish = publish;
        _logger = logger;
    }

    public async Task PublishAsync(IDomainEvent domainEvent)
    {
        _logger.LogInformation("Publishing domain event {EventName} via MassTransit", domainEvent.GetType().Name);
        await _publish.Publish(domainEvent);
    }
} 