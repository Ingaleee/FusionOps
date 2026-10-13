using FusionOps.Application.Abstractions;
using FusionOps.Application.Execution;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Execution;
using FusionOps.Domain.Shared;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Execution;

public sealed class PostgresTemporalLicenseParticipant(IDbContextFactory<FulfillmentContext> factory, TimeProvider clock) : ITemporalLicenseParticipant
{
    private const string Reserve = "license-reserve/v1", Release = "license-release/v1";
    public Task<ExecutionParticipantResult> ReserveAsync(ExecutionBaseline baseline, CancellationToken ct) => ExecuteAsync(baseline, false, ct);
    public async Task ReleaseAsync(ExecutionBaseline baseline, CancellationToken ct) => await ExecuteAsync(baseline, true, ct);
    private async Task<ExecutionParticipantResult> ExecuteAsync(ExecutionBaseline baseline, bool release, CancellationToken ct)
    {
        baseline.VerifyEvidence(); using var tenant = TenantExecution.Enter(TenantScope.For(baseline.TenantId));
        await using var db = await factory.CreateDbContextAsync(ct); db.BindScope(TenantScope.For(baseline.TenantId));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await WorkflowPersistence.LockAsync(db, baseline.Id, "temporal-license-effect", ct);
        var step = release ? Release : Reserve;
        var receipt = await db.Set<TemporalLicenseEffectReceipt>().FindAsync([baseline.Id, step], ct);
        if (receipt is not null) return Resolve(receipt, baseline);
        if (release)
        {
            var original = await db.Set<TemporalLicenseEffectReceipt>().FindAsync([baseline.Id, Reserve], ct)
                ?? throw new ExecutionPlanRuleException("execution_license_receipt_missing");
            if (!Resolve(original, baseline).Succeeded) throw new ExecutionPlanRuleException("execution_license_reservation_rejected");
        }
        var evidence = baseline.ScenarioEvidence;
        var definitions = evidence.Input.Definition.Tasks.ToDictionary(t => t.Id);
        var demands = evidence.Result.Tasks.SelectMany(t => definitions[t.TaskId].Licenses.Select(l => new { l.Product, Demand = new TemporalLicenseDemand(t.TaskId, l.Seats, t.Period) }))
            .GroupBy(d => d.Product).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
        string? rejection = null;
        try
        {
            foreach (var group in demands)
            {
                var product = group.Key;
                var rows = await db.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM license_pools WHERE \"TenantId\"={db.TenantId} AND \"Product\"={product} FOR UPDATE").ToListAsync(ct);
                if (rows.Count == 0) throw new ExecutionPlanRuleException("execution_license_pool_missing");
                var pool = await db.Set<LicensePool>().SingleAsync(p => p.Product == product, ct);
                var observation = evidence.Source.Licenses.Single(l => l.Product == product);
                if (pool.Id != observation.PoolId) throw new ExecutionPlanRuleException("execution_license_pool_changed");
                if (release)
                {
                    var owned = pool.TemporalReservations.Where(r => r.PlanId == baseline.Id).ToArray();
                    if (owned.Length != group.Count() || owned.Any(r => r.ProjectId != baseline.ProjectId || !group.Any(d => d.Demand.TaskId == r.TaskId && d.Demand.Period == r.Period && d.Demand.Seats == r.Seats)))
                        throw new ExecutionPlanRuleException("execution_license_ownership_changed");
                    pool.ReleaseTemporal(baseline.Id, baseline.ProjectId);
                }
                else pool.ReserveTemporal(baseline.Id, baseline.ProjectId, group.Select(d => d.Demand).ToArray());
            }
        }
        catch (LicenseCapacityException) when (!release) { db.ChangeTracker.Clear(); rejection = "license_capacity"; }
        catch (ExecutionPlanRuleException e) when (!release) { db.ChangeTracker.Clear(); rejection = e.Code; }
        var result = rejection is null ? ExecutionParticipantResult.Success() : ExecutionParticipantResult.Rejected(rejection);
        db.Add(TemporalLicenseEffectReceipt.Create(baseline, step, result.Succeeded, rejection, clock.GetUtcNow()));
        var sources = WorkflowPersistence.StageEvents(db, "execution-worker", baseline.Id);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); foreach (var source in sources) source.ClearDomainEvents(); return result;
    }
    private static ExecutionParticipantResult Resolve(TemporalLicenseEffectReceipt receipt, ExecutionBaseline b)
    {
        if (receipt.Digest != b.Digest || receipt.ProjectId != b.ProjectId) throw new IdempotencyConflictException();
        return receipt.Succeeded ? ExecutionParticipantResult.Success() : ExecutionParticipantResult.Rejected(receipt.RejectionCode!);
    }
}
