using MediatR;

namespace FusionOps.Application.UseCases.AllocateResource;

public sealed record AllocateCommand(Guid ProjectId, IReadOnlyCollection<Guid> ResourceIds, DateTime PeriodFrom, DateTime PeriodTo) : IRequest<Unit>; 