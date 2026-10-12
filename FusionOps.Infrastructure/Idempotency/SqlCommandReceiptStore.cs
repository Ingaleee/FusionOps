using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using FusionOps.Application.Abstractions;
using FusionOps.Domain.Shared;
using FusionOps.Infrastructure.Persistence.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Idempotency;

public sealed class SqlCommandReceiptStore(WorkforceContext context, IExecutionContext execution, TimeProvider clock) : ICommandReceiptStore
{
    private string? _lockedScope;
    private Guid? _lockedTransactionId;

    private string Scope(string operation, string key) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new[] { execution.Actor, operation, key })));

    public async Task<StoredCommandResult?> FindForUpdateAsync(string operation, string key, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Idempotency lookup requires the command transaction");
        var scope = Scope(operation, key);
        var lockResult = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var lockName = new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = $"fusionops:receipt:{scope}" };
        await context.Database.ExecuteSqlRawAsync(
            "EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;",
            [lockResult, lockName], cancellationToken);
        if ((int)lockResult.Value < 0) throw new IdempotencyInProgressException();
        _lockedScope = scope;
        _lockedTransactionId = context.Database.CurrentTransaction.TransactionId;

        // The transaction-owned application lock serializes this logical key, including first use.
        // READCOMMITTED avoids taking a broad serializable key-range lock in a sparse receipt index.
        var stored = (await context.CommandReceipts.FromSqlInterpolated(
            $"SELECT * FROM [CommandReceipts] WITH (READCOMMITTED) WHERE [ScopeHash] = {scope}")
            .AsNoTracking().ToListAsync(cancellationToken)).SingleOrDefault();
        return stored is null ? null : new(stored.Fingerprint, stored.ResultJson);
    }

    public void Stage(string operation, string key, string fingerprint, string resultJson)
    {
        var scope = Scope(operation, key);
        if (context.Database.CurrentTransaction is null || _lockedScope != scope
            || _lockedTransactionId != context.Database.CurrentTransaction.TransactionId)
            throw new InvalidOperationException("Acquire the matching receipt lock before staging a response");
        context.CommandReceipts.Add(new CommandReceipt
        {
            ScopeHash = scope, Fingerprint = fingerprint, ResultJson = resultJson,
            CompletedAt = clock.GetUtcNow().UtcDateTime
        });
    }
}
