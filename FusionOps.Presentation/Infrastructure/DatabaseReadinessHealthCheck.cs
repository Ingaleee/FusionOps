using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using FusionOps.Domain.Workflows;
using FusionOps.Infrastructure.Workflows;

namespace FusionOps.Presentation.Infrastructure;

public sealed class DatabaseReadinessHealthCheck(
    IDbContextFactory<WorkforceContext> workforce, IDbContextFactory<FulfillmentContext> fulfillment, IHostEnvironment environment) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var tenant = FusionOps.Application.Abstractions.TenantExecution.Enter(FusionOps.Application.Abstractions.TenantScope.System);
            await using var sql = await workforce.CreateDbContextAsync(timeout.Token);
            await using var postgres = await fulfillment.CreateDbContextAsync(timeout.Token);
            await sql.OutboxMessages.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await postgres.OutboxMessages.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await sql.CommandReceipts.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await sql.ResourceCatalog.AsNoTracking().Include(r => r.Skills).Take(1).ToListAsync(timeout.Token);
            await sql.PlanningQuotes.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await postgres.Set<ReservationWorkflow>().AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await postgres.Set<WorkflowEffectReceipt>().AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await postgres.Set<FusionOps.Domain.Projects.OperationalProject>().AsNoTracking().Include(p => p.Members).Take(1).ToListAsync(timeout.Token);
            await postgres.Set<FusionOps.Domain.Projects.ProjectDefinitionRevision>().AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await postgres.Set<FusionOps.Domain.Scheduling.TemporalScenario>().AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await postgres.Set<FusionOps.Domain.Execution.ExecutionBaseline>().AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await postgres.Set<FusionOps.Domain.Execution.ExecutionPlan>().AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await postgres.Set<FusionOps.Domain.Execution.ExecutionPlanDecision>().AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await postgres.Set<FusionOps.Domain.Execution.TemporalLicenseEffectReceipt>().AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await postgres.Set<FusionOps.Domain.Execution.ExecutionActivation>().AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await postgres.Set<FusionOps.Domain.Execution.ExecutionActivationDecision>().AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await postgres.Set<FusionOps.Domain.Execution.ExecutionTask>().AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await postgres.Set<FusionOps.Domain.Execution.ExecutionTaskFact>().AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await postgres.Set<FusionOps.Domain.Entities.LicensePool>().SelectMany(p => p.TemporalReservations).Select(r => r.PlanId).Take(1).ToListAsync(timeout.Token);
            if (!environment.IsDevelopment())
            {
                var workerSqlAllowed = await sql.Database.SqlQuery<int>($"SELECT CASE WHEN IS_MEMBER(N'fusionops_system') = 1 AND IS_MEMBER(N'db_owner') = 0 AND IS_SRVROLEMEMBER(N'sysadmin') = 0 THEN 1 ELSE 0 END AS Value").SingleAsync(timeout.Token);
                var workerPgAllowed = await postgres.Database.SqlQuery<bool>($"SELECT NOT rolsuper AND NOT rolbypassrls AND pg_has_role(current_user, 'fusionops_system', 'member') AS \"Value\" FROM pg_roles WHERE rolname = current_user").SingleAsync(timeout.Token);
                using var appTenant = FusionOps.Application.Abstractions.TenantExecution.Enter(FusionOps.Application.Abstractions.TenantScope.Legacy);
                await using var appSql = await workforce.CreateDbContextAsync(timeout.Token);
                await using var appPg = await fulfillment.CreateDbContextAsync(timeout.Token);
                var unsafeSql = await appSql.Database.SqlQuery<int>($"SELECT CASE WHEN IS_MEMBER(N'fusionops_system') = 1 OR IS_MEMBER(N'db_owner') = 1 OR IS_SRVROLEMEMBER(N'sysadmin') = 1 THEN 1 ELSE 0 END AS Value").SingleAsync(timeout.Token);
                var unsafePg = await appPg.Database.SqlQuery<bool>($"SELECT rolsuper OR rolbypassrls OR pg_has_role(current_user, 'fusionops_system', 'member') AS \"Value\" FROM pg_roles WHERE rolname = current_user").SingleAsync(timeout.Token);
                if (workerSqlAllowed != 1 || !workerPgAllowed || unsafeSql != 0 || unsafePg)
                    return HealthCheckResult.Unhealthy("Separate restricted application and system-worker database roles are required");
            }
            return HealthCheckResult.Healthy("Command stores, outboxes, receipts and workflow schemas are available");
        }
        catch (Exception error) { return HealthCheckResult.Unhealthy("A command store or schema is unavailable", error); }
    }
}
