namespace FusionOps.Application.Abstractions;

/// <summary>The operation and canonical fingerprint are versioned contracts, independent of HTTP serialization.</summary>
public interface IIdempotentCommand : ITransactionalRequest
{
    string? IdempotencyKey { get; }
    string Operation { get; }
    string Fingerprint { get; }
}

public sealed record StoredCommandResult(string Fingerprint, string ResultJson);

public interface ICommandReceiptStore
{
    /// <summary>Acquires the caller/operation/key lock for the duration of the current command transaction.</summary>
    Task<StoredCommandResult?> FindForUpdateAsync(string operation, string key, CancellationToken cancellationToken);
    void Stage(string operation, string key, string fingerprint, string resultJson);
}
