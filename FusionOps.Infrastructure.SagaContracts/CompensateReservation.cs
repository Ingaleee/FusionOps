using MassTransit;

namespace FusionOps.Infrastructure.SagaContracts;

public record CompensateReservation(Guid CorrelationId, Guid ProjectId, string Reason) : CorrelatedBy<Guid>; 