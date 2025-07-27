using MassTransit;

namespace FusionOps.Infrastructure.SagaContracts;

public record ReserveResources(Guid CorrelationId, Guid ProjectId) : CorrelatedBy<Guid>;