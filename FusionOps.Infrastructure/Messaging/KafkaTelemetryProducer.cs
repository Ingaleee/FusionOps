using System.Text.Json;
using Confluent.Kafka;
using FusionOps.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace FusionOps.Infrastructure.Messaging;

public class KafkaTelemetryProducer : ITelemetryProducer, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaTelemetryProducer> _logger;

    public KafkaTelemetryProducer(ILogger<KafkaTelemetryProducer> logger)
    {
        _logger = logger;
        var config = new ProducerConfig { BootstrapServers = "localhost:9092" };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync(string eventType, object payload)
    {
        var value = JsonSerializer.Serialize(payload);
        var msg = new Message<string, string> { Key = eventType, Value = value };
        await _producer.ProduceAsync("telemetry", msg);
        _logger.LogInformation("Kafka telemetry produced: {EventType}", eventType);
    }

    public void Dispose() => _producer?.Dispose();
} 