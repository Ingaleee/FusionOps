using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MediatR;

namespace FusionOps.Application.Pipelines;

public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = validators;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (_validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);
            var failures = await Task.WhenAll(_validators.Select(v => v.ValidateAsync(context, cancellationToken)));
            var errors = failures.SelectMany(r => r.Errors).Where(f => f != null).ToList();
            if (errors.Count != 0)
                throw new ValidationException(errors);
        }
        return await next();
    }
}