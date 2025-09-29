using System.Data.Common;
using FusionOps.Application.Abstractions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FusionOps.Infrastructure.Persistence.Common;

public sealed class NpgsqlSearchPathTransactionInterceptor : DbTransactionInterceptor
{
    private readonly ITenantProvider _tenantProvider;
    private readonly IOptionsMonitor<TenancyOptions> _options;

    public NpgsqlSearchPathTransactionInterceptor(ITenantProvider tenantProvider, IOptionsMonitor<TenancyOptions> options)
    {
        _tenantProvider = tenantProvider;
        _options = options;
    }

    public override void TransactionStarted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (!_tenantProvider.IsSet) return;
        if (!_options.CurrentValue.Mode.Equals("Schema", StringComparison.OrdinalIgnoreCase)) return;
        if (transaction.Connection is NpgsqlConnection npg)
        {
            using var cmd = npg.CreateCommand();
            cmd.CommandText = "SET LOCAL search_path = @schema, public";
            cmd.Parameters.AddWithValue("schema", $"t_{_tenantProvider.TenantId}");
            cmd.ExecuteNonQuery();
        }
        base.TransactionStarted(transaction, eventData);
    }
}


