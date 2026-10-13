using System.Diagnostics.Metrics;

namespace FusionOps.Infrastructure.Outbox;

public static class OutboxMetrics
{
    public const string MeterName = "FusionOps.Outbox";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Delivered = Meter.CreateCounter<long>("fusionops.outbox.delivered");
    private static readonly Counter<long> Retried = Meter.CreateCounter<long>("fusionops.outbox.retried");
    private static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>("fusionops.outbox.dead_lettered");
    private static readonly Histogram<double> DeliveryLag = Meter.CreateHistogram<double>("fusionops.outbox.delivery_lag", "s");

    public static void RecordCommitted(OutboxMessage message, string? provider, DateTime now)
    {
        var tag = new KeyValuePair<string, object?>("store", provider == "Microsoft.EntityFrameworkCore.SqlServer" ? "sqlserver" : "postgres");
        if (message.ProcessedAt is not null)
        {
            Delivered.Add(1, tag);
            DeliveryLag.Record(Math.Max(0, (now - message.OccurredOn).TotalSeconds), tag);
        }
        else if (message.DeadLetteredAt is not null) DeadLettered.Add(1, tag);
        else Retried.Add(1, tag);
    }
}
