using MassTransit;

namespace FusionOps.Infrastructure.SagaContracts;

public record AllocationCompleted(Guid CorrelationId, Guid ProjectId) : CorrelatedBy<Guid>;