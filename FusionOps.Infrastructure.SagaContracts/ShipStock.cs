using MassTransit;

namespace FusionOps.Infrastructure.SagaContracts;

public record ShipStock(Guid CorrelationId, Guid ProjectId) : CorrelatedBy<Guid>;