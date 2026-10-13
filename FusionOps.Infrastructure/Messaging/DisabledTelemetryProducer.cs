using FusionOps.Domain.Interfaces;

namespace FusionOps.Infrastructure.Messaging;

/// <summary>Kafka diagnostics are opt-in. Durable integration events still go through the outbox and broker.</summary>
public sealed class DisabledTelemetryProducer : ITelemetryProducer
{
    public Task PublishAsync(string eventType, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
