using System.Text.Json;
using FusionOps.Application.Abstractions;
using FusionOps.Domain.Shared;
using MediatR;

namespace FusionOps.Application.Pipelines;

/// <summary>Runs inside TransactionBehavior; command state, outbox and receipt share its commit.</summary>
public sealed class IdempotencyBehavior<TRequest, TResponse>(ICommandReceiptStore receipts)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IIdempotentCommand { IdempotencyKey: { } key } command) return await next();

        var stored = await receipts.FindForUpdateAsync(command.Operation, key, cancellationToken);
        if (stored is not null)
        {
            if (stored.Fingerprint != command.Fingerprint) throw new IdempotencyConflictException();
            return JsonSerializer.Deserialize<TResponse>(stored.ResultJson, Json)
                ?? throw new InvalidOperationException("The persisted command response is invalid");
        }

        var result = await next();
        receipts.Stage(command.Operation, key, command.Fingerprint, JsonSerializer.Serialize(result, Json));
        return result;
    }
}
