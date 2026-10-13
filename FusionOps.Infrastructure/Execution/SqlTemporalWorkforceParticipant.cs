using System.Data;
using System.Text.Json;
using FusionOps.Application.Abstractions;
using FusionOps.Application.Execution;
using FusionOps.Domain.Catalog;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Execution;
using FusionOps.Domain.Shared;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Infrastructure.Idempotency;
using FusionOps.Infrastructure.Outbox;
using FusionOps.Infrastructure.Persistence.SqlServer;
using FusionOps.Infrastructure.Planning;
using FusionOps.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Execution;

/// <summary>One atomic decision for every task interval, with an original positive or negative SQL receipt.</summary>
public sealed class SqlTemporalWorkforceParticipant(IDbContextFactory<WorkforceContext> factory, TimeProvider clock) : ITemporalWorkforceParticipant
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public Task<ExecutionParticipantResult> ReserveAsync(ExecutionBaseline baseline, CancellationToken ct) => ExecuteAsync(baseline, false, ct);
    public async Task ReleaseAsync(ExecutionBaseline baseline, CancellationToken ct) => await ExecuteAsync(baseline, true, ct);
    private async Task<ExecutionParticipantResult> ExecuteAsync(ExecutionBaseline baseline, bool release, CancellationToken ct)
    {
        baseline.VerifyEvidence(); using var tenant = TenantExecution.Enter(TenantScope.For(baseline.TenantId));
        await using var db = await factory.CreateDbContextAsync(ct); db.BindScope(TenantScope.For(baseline.TenantId));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var receipts = new SqlCommandReceiptStore(db, new ParticipantExecution(baseline.Id), clock);
        var step = release ? "execution.workforce-release/v1" : "execution.workforce-reserve/v1";
        var key = baseline.Id.ToString("N");
        var existing = await receipts.FindForUpdateAsync(step, key, ct);
        if (existing is not null)
        {
            if (existing.Fingerprint != baseline.Digest) throw new IdempotencyConflictException();
            return JsonSerializer.Deserialize<ExecutionParticipantResult>(existing.ResultJson, Json) ?? throw new InvalidDataException("Missing participant result");
        }
        var evidence = baseline.ScenarioEvidence;
        var periods = evidence.Result.Tasks.SelectMany(t => t.Resources.Select(r => new { t.TaskId, t.Period, r.ResourceId })).ToArray();
        var repo = new AllocationRepository(db); ExecutionParticipantResult result;
        if (release)
        {
            var original = await receipts.FindForUpdateAsync("execution.workforce-reserve/v1", key, ct) ?? throw new ExecutionPlanRuleException("execution_resource_receipt_missing");
            if (original.Fingerprint != baseline.Digest) throw new IdempotencyConflictException();
            var reserved = JsonSerializer.Deserialize<ExecutionParticipantResult>(original.ResultJson, Json) ?? throw new InvalidDataException("Missing reservation result");
            if (!reserved.Succeeded) throw new ExecutionPlanRuleException("execution_resource_reservation_rejected");
            foreach (var resourceId in periods.Select(p => p.ResourceId).Distinct().Order())
            {
                var calendar = await repo.FindForResourceWithLockAsync(resourceId, ct, requireActive: false);
                foreach (var owned in reserved.Allocations.Where(a => a.ResourceId == resourceId))
                {
                    var allocation = calendar.SingleOrDefault(a => a.Id.Value == owned.AllocationId) ?? throw new ExecutionPlanRuleException("execution_resource_ownership_missing");
                    var expected = periods.Single(p => p.TaskId == owned.TaskId && p.ResourceId == resourceId);
                    if (allocation.ProjectId != baseline.ProjectId || allocation.Period != expected.Period) throw new ExecutionPlanRuleException("execution_resource_ownership_changed");
                    allocation.Cancel();
                }
            }
            result = ExecutionParticipantResult.Success();
            // FindForUpdate selected the reserve lock; reacquire the release scope before writing its receipt.
            await receipts.FindForUpdateAsync(step, key, ct);
        }
        else
        {
            try
            {
                var allocations = new List<ExecutionAllocation>();
                foreach (var resourceId in periods.Select(p => p.ResourceId).Distinct().Order())
                {
                    var resource = await CatalogLocks.RequireAsync(db, resourceId, true, ct);
                    if (resource.Revision != evidence.Source.Resources.Single(r => r.Id == resourceId).Revision)
                        throw new ResourceAdmissionException("catalog_changed", resourceId);
                    var calendar = (await repo.FindForResourceWithLockAsync(resourceId, ct)).ToList();
                    foreach (var use in periods.Where(p => p.ResourceId == resourceId).OrderBy(p => p.Period.Start).ThenBy(p => p.TaskId))
                    {
                        var allocation = Allocation.Reserve(resourceId, baseline.ProjectId, use.Period, calendar);
                        db.Add(allocation); calendar.Add(allocation); allocations.Add(new(use.TaskId, resourceId, allocation.Id.Value));
                    }
                }
                result = ExecutionParticipantResult.Success(allocations);
            }
            catch (ResourceAdmissionException e) { db.ChangeTracker.Clear(); result = ExecutionParticipantResult.Rejected(e.Code); }
            catch (AllocationConflictException) { db.ChangeTracker.Clear(); result = ExecutionParticipantResult.Rejected("resource_conflict"); }
        }
        foreach (var allocation in db.ChangeTracker.Entries<Allocation>().Select(e => e.Entity).ToArray())
            foreach (var domainEvent in allocation.DomainEvents) db.OutboxMessages.Add(DomainEventSerializer.Serialize(domainEvent, "execution-worker", $"execution.{baseline.Id:N}", baseline.TenantId));
        receipts.Stage(step, key, baseline.Digest, JsonSerializer.Serialize(result, Json));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
    }
    private sealed record ParticipantExecution(Guid PlanId) : IExecutionContext
    {
        public string Actor => "execution-worker";
        public string CorrelationId => $"execution.{PlanId:N}";
    }
}
