using FusionOps.Domain.Interfaces;
using MediatR;

namespace FusionOps.Application.Abstractions;

public interface ITransactionalRequest
{
    PersistenceBoundary Boundary { get; }
}

public interface ICommand<out TResponse> : IRequest<TResponse>, ITransactionalRequest { }
