using System;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;

namespace FusionOps.Infrastructure.Saga;

public class AllocationState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = string.Empty;
    public Guid ProjectId { get; set; }
    public DateTime? ReservedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
}