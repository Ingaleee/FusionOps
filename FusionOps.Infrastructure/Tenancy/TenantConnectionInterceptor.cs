using System.Data.Common;
using FusionOps.Domain.Tenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FusionOps.Infrastructure.Tenancy;

public sealed class TenantConnectionInterceptor(TenantDbContext context) : DbConnectionInterceptor
{
    private DbCommand Create(DbConnection connection)
    {
        context.SealScope();
        var command = connection.CreateCommand();
        command.CommandText = connection is Microsoft.Data.SqlClient.SqlConnection
            ? "EXEC sys.sp_set_session_context @key=N'fusionops.tenant', @value=@tenant, @read_only=1; EXEC sys.sp_set_session_context @key=N'fusionops.system', @value=@system, @read_only=1;"
            : "SELECT set_config('fusionops.tenant', @tenant, false), set_config('fusionops.system', @system, false);";
        var tenant = command.CreateParameter(); tenant.ParameterName = "tenant"; tenant.Value = context.TenantId.ToString("D"); command.Parameters.Add(tenant);
        var system = command.CreateParameter(); system.ParameterName = "system"; system.Value = context.SystemAccess ? "on" : "off"; command.Parameters.Add(system);
        return command;
    }
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = Create(connection);
        command.ExecuteNonQuery();
    }
    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = Create(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
