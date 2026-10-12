using System.Threading;
using System.Threading.Tasks;
using FusionOps.Domain.Interfaces;
using MediatR;
using FusionOps.Application.Abstractions;

namespace FusionOps.Application.Pipelines;

public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    private readonly IUnitOfWork _uow;
    public TransactionBehavior(IUnitOfWork uow) => _uow = uow;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ITransactionalRequest command)
            return await next();

        await _uow.BeginAsync(command.Boundary, cancellationToken);
        try
        {
            var response = await next();
            await _uow.CommitAsync(cancellationToken);
            return response;
        }
        catch
        {
            // Cleanup must still run when the caller cancels the request.
            await _uow.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
