using System;

namespace FusionOps.Infrastructure.Outbox;

public class OutboxMessage
{
    public Guid Id { get; set; }
    public DateTime OccurredOn { get; set; }
    public string Type { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public DateTime? ProcessedAt { get; set; }
    public string Actor { get; set; } = "system";
    public string CorrelationId { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? DeadLetteredAt { get; set; }
    public string? LastError { get; set; }

    public void RecordFailure(Exception error, DateTime now, int maxAttempts)
    {
        Attempts++;
        LastError = error.Message.Length <= 2000 ? error.Message : error.Message[..2000];
        if (Attempts >= maxAttempts)
        {
            DeadLetteredAt = now;
            NextAttemptAt = null;
        }
        else
        {
            NextAttemptAt = now.AddSeconds(Math.Min(300, Math.Pow(2, Attempts)));
        }
    }
}
