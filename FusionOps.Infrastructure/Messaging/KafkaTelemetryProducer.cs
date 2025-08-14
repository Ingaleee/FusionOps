using System.Text.Json;
using Confluent.Kafka;
using FusionOps.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Polly;

namespace FusionOps.Infrastructure.Messaging;

public class KafkaTelemetryProducer : ITelemetryProducer, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaTelemetryProducer> _logger;
    private readonly AsyncPolicy _circuitBreaker;

    public KafkaTelemetryProducer(ILogger<KafkaTelemetryProducer> logger)
    {
        _logger = logger;
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
        await _circuitBreaker.ExecuteAsync(async () =>
        {
            await _producer.ProduceAsync("telemetry", msg);
            _logger.LogInformation("Kafka telemetry produced: {EventType}", eventType);
        });
    }

    public void Dispose() => _producer?.Dispose();
}