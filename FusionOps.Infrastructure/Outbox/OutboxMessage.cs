using System;

namespace FusionOps.Infrastructure.Outbox;

public class OutboxMessage
{
    public Guid Id { get; set; }
    public DateTime OccurredOn { get; set; }
    public string Type { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public DateTime? ProcessedAt { get; set; }
} 