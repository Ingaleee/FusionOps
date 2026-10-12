using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Shared.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;
using Polly;

namespace FusionOps.Infrastructure.Messaging;

public class RabbitBus : IEventBus
{
    private readonly IBus _bus;
    private readonly ILogger<RabbitBus> _logger;
    private readonly AsyncPolicy _retryPolicy;

    public RabbitBus(IBus bus, ILogger<RabbitBus> logger)
    {
        _bus = bus;
        _logger = logger;
        _retryPolicy = Policy.Handle<Exception>()
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                (ex, ts, attempt, ctx) =>
                {
                    _logger.LogWarning(ex, "RabbitMQ publish retry {Attempt}", attempt);
                });
    }

    public async Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Publishing domain event {EventName} via MassTransit", domainEvent.GetType().Name);
        await _retryPolicy.ExecuteAsync(ct => _bus.Publish(domainEvent, domainEvent.GetType(),
            context => context.MessageId = domainEvent.Id, ct), cancellationToken);
    }
}
