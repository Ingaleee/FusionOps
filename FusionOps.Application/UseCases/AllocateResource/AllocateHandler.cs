using MediatR;

namespace FusionOps.Application.UseCases.AllocateResource;

public class AllocateHandler : IRequestHandler<AllocateCommand, Unit>
{
    public Task<Unit> Handle(AllocateCommand request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
} 