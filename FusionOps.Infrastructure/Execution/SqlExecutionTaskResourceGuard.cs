using System.Data;
using FusionOps.Application.Abstractions;
using FusionOps.Application.Execution;
using FusionOps.Domain.Execution;
using FusionOps.Infrastructure.Persistence.SqlServer;
using FusionOps.Infrastructure.Planning;
using FusionOps.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Execution;
public sealed class SqlExecutionTaskResourceGuard(IDbContextFactory<WorkforceContext> factory) : IExecutionTaskResourceGuard
{
    public async Task<string?> CheckAsync(ExecutionActivation activation, Guid taskId, CancellationToken ct)
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(activation.TenantId));
        await using var db = await factory.CreateDbContextAsync(ct); db.BindScope(TenantScope.For(activation.TenantId));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var task = activation.Baseline.ScenarioEvidence.Result.Tasks.Single(t => t.TaskId == taskId);
        var repo = new AllocationRepository(db);
        foreach (var resourceId in task.Resources.Select(r => r.ResourceId).Order())
        {
            FusionOps.Domain.Catalog.CatalogResource resource;
            try { resource = await CatalogLocks.RequireAsync(db, resourceId, true, ct); }
            catch (FusionOps.Domain.Catalog.ResourceAdmissionException e) { return e.Code; }
            if (resource.Revision != activation.Baseline.ScenarioEvidence.Source.Resources.Single(r => r.Id == resourceId).Revision) return "execution_task_catalog_changed";
            var owned = activation.ResourceOwnership.SingleOrDefault(r => r.TaskId == taskId && r.ResourceId == resourceId);
            if (owned is null) return "execution_task_resource_ownership_missing";
            var calendar = await repo.FindForResourceWithLockAsync(resourceId, ct);
            if (!calendar.Any(a => a.Id.Value == owned.AllocationId && !a.IsCancelled && a.ProjectId == activation.ProjectId && a.Period == task.Period)) return "execution_task_resource_ownership_changed";
        }
        await tx.CommitAsync(ct); return null;
    }
}
