using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Shared.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;
using Polly;

namespace FusionOps.Infrastructure.Messaging;

public class RabbitBus : IEventBus
{
    private readonly IPublishEndpoint _publish;
    private readonly ILogger<RabbitBus> _logger;
    private readonly AsyncPolicy _retryPolicy;

    public RabbitBus(IPublishEndpoint publish, ILogger<RabbitBus> logger)
    {
        _publish = publish;
        _logger = logger;
        _retryPolicy = Policy.Handle<Exception>()
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                (ex, ts, attempt, ctx) =>
                {
                    _logger.LogWarning(ex, "RabbitMQ publish retry {Attempt}", attempt);
                });
    }

    public async Task PublishAsync(IDomainEvent domainEvent)
    {
        _logger.LogInformation("Publishing domain event {EventName} via MassTransit", domainEvent.GetType().Name);
        await _retryPolicy.ExecuteAsync(() => _publish.Publish(domainEvent));
    }
} 