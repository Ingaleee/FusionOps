namespace FusionOps.Infrastructure.Workflows;

/// <summary>Durable participant inbox; committed with the corresponding license effect.</summary>
public sealed class WorkflowEffectReceipt
{
    public Guid WorkflowId { get; set; }
    public string Step { get; set; } = string.Empty;
    public string SpecificationHash { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public string? RejectionCode { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
}
