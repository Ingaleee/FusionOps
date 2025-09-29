using System.Text.Json;
using Confluent.Kafka;
using FusionOps.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Polly;
using FusionOps.Application.Abstractions;

namespace FusionOps.Infrastructure.Messaging;

public class KafkaTelemetryProducer : ITelemetryProducer, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaTelemetryProducer> _logger;
    private readonly ITenantProvider _tenantProvider;
    private readonly AsyncPolicy _circuitBreaker;

    public KafkaTelemetryProducer(ILogger<KafkaTelemetryProducer> logger, ITenantProvider tenantProvider)
    {
        _logger = logger;
        _tenantProvider = tenantProvider;
        var host = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP") ?? "localhost:9092";
        var config = new ProducerConfig { BootstrapServers = host };
        _producer = new ProducerBuilder<string, string>(config).Build();
        _circuitBreaker = Policy.Handle<Exception>()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30),
                (ex, ts) => _logger.LogWarning(ex, "Kafka circuit opened for {Duration}s", ts.TotalSeconds),
                () => _logger.LogInformation("Kafka circuit closed"));
    }

    public async Task PublishAsync(string eventType, object payload)
    {
        var value = JsonSerializer.Serialize(payload);
        var msg = new Message<string, string> { Key = eventType, Value = value };
        if (_tenantProvider.IsSet)
        {
            msg.Headers ??= new Headers();
            msg.Headers.Add("tenant_id", System.Text.Encoding.UTF8.GetBytes(_tenantProvider.TenantId));
        }
        await _circuitBreaker.ExecuteAsync(async () =>
        {
            await _producer.ProduceAsync("telemetry", msg);
            _logger.LogInformation("Kafka telemetry produced: {EventType}", eventType);
        });
    }

    public void Dispose() => _producer?.Dispose();
}