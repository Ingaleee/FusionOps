using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FusionOps.Presentation.BackgroundServices;

public class CdcKafkaListener : BackgroundService
{
    private readonly ILogger<CdcKafkaListener> _log;
    private readonly IConfiguration _cfg;
    private IConsumer<string, string>? _consumer;

    public CdcKafkaListener(ILogger<CdcKafkaListener> log, IConfiguration cfg)
    {
        _log = log;
        _cfg = cfg;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var conf = new ConsumerConfig
        {
            BootstrapServers = _cfg["Kafka:Host"] ?? "kafka:9092",
            GroupId = "fusionops-cdc",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };
        _consumer = new ConsumerBuilder<string, string>(conf).Build();
        _consumer.Subscribe("dbserver1.dbo.Allocations");

        return Task.Run(() =>
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var cr = _consumer.Consume(stoppingToken);
                    _log.LogInformation("CDC Allocation event offset {Offset}: {Payload}", cr.Offset, cr.Message.Value);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error consuming CDC topic");
                    Task.Delay(1000, stoppingToken).Wait(stoppingToken);
                }
            }
        }, stoppingToken);
    }

    public override void Dispose()
    {
        _consumer?.Close();
        _consumer?.Dispose();
        base.Dispose();
    }
}