using System.Data.Common;
using FusionOps.Application.Abstractions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace FusionOps.Infrastructure.Persistence.Common;

public sealed class NpgsqlTenantConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ITenantProvider _tenantProvider;
    public NpgsqlTenantConnectionInterceptor(ITenantProvider tenantProvider) => _tenantProvider = tenantProvider;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (connection is NpgsqlConnection npg && _tenantProvider.IsSet)
        {
            using var cmd = npg.CreateCommand();
            cmd.CommandText = "SET app.tenant_id = @t";
            cmd.Parameters.AddWithValue("t", _tenantProvider.TenantId);
            cmd.ExecuteNonQuery();
        }
        base.ConnectionOpened(connection, eventData);
    }
}


