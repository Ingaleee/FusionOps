using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FusionOps.Application.Workflows;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Events;
using FusionOps.Domain.Workflows;
using FusionOps.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using FusionOps.Infrastructure.Persistence.Postgres;
using Npgsql;

namespace FusionOps.Integration.Tests;

public sealed partial class ReliabilityTests
{
    private PostgresReservationWorkflowStore WorkflowStore() => new(new TestContextFactory(_fulfillmentOptions), TimeProvider.System);
    private SqlWorkforceWorkflowParticipant WorkforceParticipant() => new(new WorkforceContextFactory(_workforceOptions),
        new TestContextFactory(_fulfillmentOptions), TimeProvider.System);
    private PostgresLicenseWorkflowParticipant LicenseParticipant() => new(new TestContextFactory(_fulfillmentOptions), TimeProvider.System);
    private ReservationWorkflowRunner WorkflowRunner(IWorkforceWorkflowParticipant? workforce = null, ILicenseWorkflowParticipant? licenses = null) =>
        new(WorkflowStore(), workforce ?? WorkforceParticipant(), licenses ?? LicenseParticipant(), NullLogger<ReservationWorkflowRunner>.Instance);
    private async Task<ReservationWorkflow> ReloadWorkflow(Guid id) =>
        await WorkflowStore().GetAsync(id, default) ?? throw new InvalidOperationException("Workflow missing");

    private async Task<(ReservationWorkflow Workflow, string Product)> PrepareWorkflow(int capacity = 5, int requested = 2)
    {
        var product = $"compiler-{Guid.NewGuid():N}";
        await using (var seed = Fulfillment())
        {
            seed.Add(new LicensePool(product, capacity));
            await seed.SaveChangesAsync();
        }
        var workflow = await ProposeAndApprove(Guid.NewGuid(), [Guid.NewGuid(), Guid.NewGuid()], [new(product, requested)]);
        return (workflow, product);
    }

    private async Task<ReservationWorkflow> ProposeAndApprove(Guid project, Guid[] resources, LicenseRequirement[] requirements)
    {
        var proposal = ReservationWorkflow.Propose(Guid.NewGuid(), new(project, resources, Period, requirements), "proposer", DateTimeOffset.UtcNow);
        await WorkflowStore().CreateAsync(proposal, default);
        return (await WorkflowStore().MutateAsync(proposal.Id, 1, WorkflowMutation.Approve, "reviewer", true, default))!;
    }

    private async Task<ReservationWorkflow> FinishWorkflow(Guid id)
    {
        for (var i = 0; i < 12; i++)
        {
            var workflow = await ReloadWorkflow(id);
            if (!ReservationWorkflow.IsExecutable(workflow.State)) return workflow;
            Assert.That(await WorkflowRunner().RunOnceAsync(default), Is.True);
        }
        throw new InvalidOperationException("Workflow did not converge");
    }

    private async Task ExpireLease(Guid id)
    {
        await using var context = Fulfillment();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE reservation_workflows SET \"LeaseExpiresAt\" = clock_timestamp() - interval '1 second' WHERE \"Id\" = {id}");
    }

    [Test]
    public async Task ApprovedWorkflowReservesBothStoresAndCommitsVersionedHistory()
    {
        var (workflow, product) = await PrepareWorkflow();
        var final = await FinishWorkflow(workflow.Id);
        Assert.That(final.State, Is.EqualTo(WorkflowState.Succeeded));
        Assert.That(final.Outcome, Is.EqualTo(WorkflowOutcome.Successful));
        Assert.That(final.Transitions.Select(t => t.Version).Order(), Is.EqualTo(new long[] { 1, 2, 3, 4 }));
        await using var sql = Workforce();
        await using var pg = Fulfillment();
        Assert.That(await sql.Allocations.CountAsync(a => !a.IsCancelled), Is.EqualTo(2));
        Assert.That(await sql.CommandReceipts.CountAsync(), Is.EqualTo(1));
        var pool = await pg.Set<LicensePool>().SingleAsync(p => p.Product == product);
        Assert.That(pool.AllocatedSeats, Is.EqualTo(2));
        Assert.That(pool.Allocations.Single().ReservationId, Is.EqualTo(workflow.Id));
        Assert.That(await pg.Set<WorkflowEffectReceipt>().CountAsync(), Is.EqualTo(1));
        Assert.That(await pg.OutboxMessages.CountAsync(m => m.Type == "v1.ReservationWorkflowChanged"), Is.EqualTo(4));
    }

    [Test]
    public async Task LicenseCapacityRejectionCompensatesTheWholeWorkforceBatch()
    {
        var (workflow, product) = await PrepareWorkflow(capacity: 1);
        var final = await FinishWorkflow(workflow.Id);
        Assert.That(final.State, Is.EqualTo(WorkflowState.Compensated));
        Assert.That(final.Outcome, Is.EqualTo(WorkflowOutcome.Rejected));
        Assert.That(final.FailureCode, Is.EqualTo("license_capacity"));
        await using var sql = Workforce();
        await using var pg = Fulfillment();
        Assert.That(await sql.Allocations.CountAsync(a => !a.IsCancelled), Is.Zero);
        Assert.That(await sql.Allocations.CountAsync(a => a.IsCancelled), Is.EqualTo(2));
        Assert.That(await sql.OutboxMessages.CountAsync(m => m.Type == "v1.ResourceAllocationCancelled"), Is.EqualTo(2));
        Assert.That(await sql.CommandReceipts.CountAsync(), Is.EqualTo(2));
        Assert.That((await pg.Set<LicensePool>().SingleAsync(p => p.Product == product)).AllocatedSeats, Is.Zero);
        var decision = await pg.Set<WorkflowEffectReceipt>().SingleAsync();
        Assert.That(decision.Succeeded, Is.False);
        Assert.That(decision.RejectionCode, Is.EqualTo("license_capacity"));
    }

    [Test]
    public async Task CrashAfterResourceCommitReplaysItsReceiptAndFencesTheOldWorker()
    {
        var (workflow, _) = await PrepareWorkflow();
        var old = (await WorkflowStore().ClaimAsync(TimeSpan.FromSeconds(45), default))!;
        var originalIds = await WorkforceParticipant().ReserveAsync(old.Workflow, default);
        await ExpireLease(workflow.Id);
        var fresh = (await WorkflowStore().ClaimAsync(TimeSpan.FromSeconds(45), default))!;
        Assert.That(fresh.Epoch, Is.GreaterThan(old.Epoch));
        Assert.That(await WorkflowStore().CompleteAsync(old, WorkflowStepResult.Success(originalIds), 5, default), Is.False);
        var replayIds = await WorkforceParticipant().ReserveAsync(fresh.Workflow, default);
        Assert.That(replayIds, Is.EqualTo(originalIds));
        Assert.That(await WorkflowStore().CompleteAsync(fresh, WorkflowStepResult.Success(replayIds), 5, default), Is.True);
        Assert.That((await FinishWorkflow(workflow.Id)).State, Is.EqualTo(WorkflowState.Succeeded));
        await using var sql = Workforce();
        Assert.That(await sql.Allocations.CountAsync(), Is.EqualTo(2));
        Assert.That(await sql.OutboxMessages.CountAsync(), Is.EqualTo(2));
        Assert.That(await sql.CommandReceipts.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task CrashAfterLicenseCommitReplaysTheParticipantInboxWithoutAllocatingAgain()
    {
        var (workflow, product) = await PrepareWorkflow();
        await WorkflowRunner().RunOnceAsync(default);
        var old = (await WorkflowStore().ClaimAsync(TimeSpan.FromSeconds(45), default))!;
        await LicenseParticipant().ReserveAsync(old.Workflow, default);
        await ExpireLease(workflow.Id);
        Assert.That((await FinishWorkflow(workflow.Id)).State, Is.EqualTo(WorkflowState.Succeeded));
        Assert.That(await WorkflowStore().CompleteAsync(old, WorkflowStepResult.Success(), 5, default), Is.False);
        await using var pg = Fulfillment();
        var pool = await pg.Set<LicensePool>().SingleAsync(p => p.Product == product);
        Assert.That(pool.AllocatedSeats, Is.EqualTo(2));
        Assert.That(pool.Version, Is.EqualTo(1));
        Assert.That(await pg.Set<WorkflowEffectReceipt>().CountAsync(), Is.EqualTo(1));
        Assert.That(await pg.OutboxMessages.CountAsync(m => m.Type == "license_seats_allocated"), Is.EqualTo(1));
    }

    [TestCase(false), TestCase(true)]
    public async Task CancellationDuringAnInFlightStepCompensatesItsCommittedOutcome(bool duringLicenses)
    {
        var (workflow, product) = await PrepareWorkflow();
        if (duringLicenses) await WorkflowRunner().RunOnceAsync(default);
        var claim = (await WorkflowStore().ClaimAsync(TimeSpan.FromSeconds(45), default))!;
        WorkflowStepResult result;
        if (duringLicenses) { await LicenseParticipant().ReserveAsync(claim.Workflow, default); result = WorkflowStepResult.Success(); }
        else result = WorkflowStepResult.Success(await WorkforceParticipant().ReserveAsync(claim.Workflow, default));
        var current = await ReloadWorkflow(workflow.Id);
        await WorkflowStore().MutateAsync(workflow.Id, current.Version, WorkflowMutation.Cancel, "operator", true, default);
        Assert.That(await WorkflowStore().CompleteAsync(claim, result, 5, default), Is.True);
        var final = await FinishWorkflow(workflow.Id);
        Assert.That(final.State, Is.EqualTo(WorkflowState.Compensated));
        Assert.That(final.Outcome, Is.EqualTo(WorkflowOutcome.UserCancelled));
        await using var sql = Workforce();
        await using var pg = Fulfillment();
        Assert.That(await sql.Allocations.CountAsync(a => !a.IsCancelled), Is.Zero);
        Assert.That((await pg.Set<LicensePool>().SingleAsync(p => p.Product == product)).AllocatedSeats, Is.Zero);
    }

    [Test]
    public async Task CompensationPreservesOtherWorkflowAndOrdinaryLicenseOwnershipAndCanBeReplayed()
    {
        var product = $"compiler-{Guid.NewGuid():N}";
        var project = Guid.NewGuid();
        await using (var seed = Fulfillment())
        {
            var pool = new LicensePool(product, 20);
            pool.Allocate(project, 3);
            pool.ClearDomainEvents();
            seed.Add(pool);
            await seed.SaveChangesAsync();
        }
        var first = await ProposeAndApprove(project, [Guid.NewGuid()], [new(product, 4)]);
        await FinishWorkflow(first.Id);
        var second = await ProposeAndApprove(project, [Guid.NewGuid()], [new(product, 5)]);
        await FinishWorkflow(second.Id);
        var successful = await ReloadWorkflow(first.Id);
        await WorkflowStore().MutateAsync(first.Id, successful.Version, WorkflowMutation.Cancel, "operator", true, default);
        var compensated = await FinishWorkflow(first.Id);
        await using var sql = Workforce();
        await using var pg = Fulfillment();
        var beforeSqlEvents = await sql.OutboxMessages.CountAsync();
        var beforePgEvents = await pg.OutboxMessages.CountAsync();
        Assert.That(await WorkforceParticipant().ReserveAsync(compensated, default), Is.EqualTo(compensated.AllocationIds));
        await LicenseParticipant().ReserveAsync(compensated, default);
        await WorkforceParticipant().ReleaseAsync(compensated, default);
        await LicenseParticipant().ReleaseAsync(compensated, default);
        Assert.That(await sql.OutboxMessages.CountAsync(), Is.EqualTo(beforeSqlEvents));
        Assert.That(await pg.OutboxMessages.CountAsync(), Is.EqualTo(beforePgEvents));
        var remaining = await pg.Set<LicensePool>().SingleAsync(p => p.Product == product);
        Assert.That(remaining.AllocatedSeats, Is.EqualTo(8));
        Assert.That(remaining.Allocations.Any(a => a.ReservationId == second.Id && a.Seats == 5), Is.True);
        Assert.That(remaining.Allocations.Any(a => a.ReservationId is null && a.Seats == 3), Is.True);
        Assert.That(await sql.Allocations.CountAsync(a => !a.IsCancelled), Is.EqualTo(1));
    }

    [Test]
    public async Task LostResourceReplyRequiresOperatorResumeAndResolvesTheSameCommittedEffect()
    {
        var (workflow, _) = await PrepareWorkflow();
        await WorkflowRunner(new LoseResourceReply(WorkforceParticipant())).RunOnceAsync(default, maxAttempts: 1);
        var paused = await ReloadWorkflow(workflow.Id);
        Assert.That(paused.State, Is.EqualTo(WorkflowState.NeedsAttention));
        Assert.That(paused.PausedStep, Is.EqualTo(WorkflowState.ReservingResources));
        Assert.That(paused.AllocationIds, Is.Empty);
        await using var sql = Workforce();
        Assert.That(await sql.Allocations.CountAsync(a => !a.IsCancelled), Is.EqualTo(2));
        await WorkflowStore().MutateAsync(paused.Id, paused.Version, WorkflowMutation.Resume, "operator", true, default);
        Assert.That((await FinishWorkflow(workflow.Id)).State, Is.EqualTo(WorkflowState.Succeeded));
        Assert.That(await sql.Allocations.CountAsync(), Is.EqualTo(2));
        Assert.That(await sql.OutboxMessages.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task LostCompensationReplyResumesWithoutReleasingAnotherOwnersSeats()
    {
        var (workflow, product) = await PrepareWorkflow();
        var successful = await FinishWorkflow(workflow.Id);
        await WorkflowStore().MutateAsync(successful.Id, successful.Version, WorkflowMutation.Cancel, "operator", true, default);
        await WorkflowRunner(licenses: new LoseLicenseReleaseReply(LicenseParticipant())).RunOnceAsync(default, maxAttempts: 1);
        var paused = await ReloadWorkflow(workflow.Id);
        Assert.That(paused.State, Is.EqualTo(WorkflowState.NeedsAttention));
        Assert.That(paused.PausedStep, Is.EqualTo(WorkflowState.ReleasingLicenses));
        await using var pg = Fulfillment();
        Assert.That((await pg.Set<LicensePool>().AsNoTracking().SingleAsync(p => p.Product == product)).AllocatedSeats, Is.Zero);
        await WorkflowStore().MutateAsync(paused.Id, paused.Version, WorkflowMutation.Resume, "operator", true, default);
        Assert.That((await FinishWorkflow(workflow.Id)).State, Is.EqualTo(WorkflowState.Compensated));
        Assert.That(await pg.OutboxMessages.CountAsync(m => m.Type == "v1.LicenseSeatsReleased"), Is.EqualTo(1));
        Assert.That(await pg.Set<WorkflowEffectReceipt>().CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task LostWorkforceCompensationReplyReplaysCancellationWithoutNewEvents()
    {
        var (workflow, _) = await PrepareWorkflow(capacity: 0);
        await WorkflowRunner().RunOnceAsync(default);
        await WorkflowRunner().RunOnceAsync(default);
        Assert.That((await ReloadWorkflow(workflow.Id)).State, Is.EqualTo(WorkflowState.ReleasingResources));
        await WorkflowRunner(new LoseResourceReleaseReply(WorkforceParticipant())).RunOnceAsync(default, maxAttempts: 1);
        var paused = await ReloadWorkflow(workflow.Id);
        Assert.That(paused.State, Is.EqualTo(WorkflowState.NeedsAttention));
        Assert.That(paused.PausedStep, Is.EqualTo(WorkflowState.ReleasingResources));
        await using var sql = Workforce();
        Assert.That(await sql.Allocations.CountAsync(a => !a.IsCancelled), Is.Zero);
        await WorkflowStore().MutateAsync(paused.Id, paused.Version, WorkflowMutation.Resume, "operator", true, default);
        Assert.That((await FinishWorkflow(workflow.Id)).State, Is.EqualTo(WorkflowState.Compensated));
        Assert.That(await sql.OutboxMessages.CountAsync(m => m.Type == "v1.ResourceAllocationCancelled"), Is.EqualTo(2));
        Assert.That(await sql.CommandReceipts.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task ConcurrentWorkersLeaseDistinctRowsAndSkipHeldCoordinatorLocks()
    {
        var first = await ProposeAndApprove(Guid.NewGuid(), [Guid.NewGuid()], []);
        var second = await ProposeAndApprove(Guid.NewGuid(), [Guid.NewGuid()], []);
        await using var held = Fulfillment();
        await using var transaction = await held.Database.BeginTransactionAsync();
        await held.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM reservation_workflows WHERE \"Id\" = {first.Id} FOR UPDATE").ToListAsync();
        var claims = await Task.WhenAll(WorkflowStore().ClaimAsync(TimeSpan.FromSeconds(45), default),
            WorkflowStore().ClaimAsync(TimeSpan.FromSeconds(45), default)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(claims.Count(c => c is not null), Is.EqualTo(1));
        Assert.That(claims.Single(c => c is not null)!.Workflow.Id, Is.EqualTo(second.Id));
        await transaction.RollbackAsync();
        var another = await WorkflowStore().ClaimAsync(TimeSpan.FromSeconds(45), default);
        Assert.That(another!.Workflow.Id, Is.EqualTo(first.Id));
        Assert.That(await WorkflowStore().ClaimAsync(TimeSpan.FromSeconds(45), default), Is.Null);
    }

    [Test]
    public async Task DeadlineExpiryRequestsCompensationWithoutForgettingTheResourceReceipt()
    {
        var (workflow, product) = await PrepareWorkflow();
        await using (var context = Fulfillment())
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE reservation_workflows SET \"DeadlineAt\" = clock_timestamp() - interval '1 second' WHERE \"Id\" = {workflow.Id}");
        var final = await FinishWorkflow(workflow.Id);
        Assert.That(final.State, Is.EqualTo(WorkflowState.Compensated));
        Assert.That(final.Outcome, Is.EqualTo(WorkflowOutcome.DeadlineExceeded));
        Assert.That(final.AllocationIds.Count, Is.EqualTo(2));
        await using var sql = Workforce();
        await using var pg = Fulfillment();
        Assert.That(await sql.Allocations.CountAsync(a => !a.IsCancelled), Is.Zero);
        Assert.That((await pg.Set<LicensePool>().SingleAsync(p => p.Product == product)).AllocatedSeats, Is.Zero);
    }

    [Test]
    public async Task ProposalReplayAndETagApprovalAreRealHttpContracts()
    {
        await using var factory = new ReservationFactory(_workforceOptions, _fulfillmentOptions);
        using var client = factory.CreateClient();
        var proposal = new { workflowId = Guid.NewGuid(), projectId = Guid.NewGuid(), resourceIds = new[] { Guid.NewGuid() },
            periodFrom = Period.Start, periodTo = Period.End, licenses = Array.Empty<LicenseRequirement>() };
        using var created = await client.PostAsJsonAsync("/api/v1/workflows", proposal);
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        Assert.That(created.Headers.ETag!.Tag, Is.EqualTo("\"1\""));
        var location = created.Headers.Location!.OriginalString;
        using var replay = await client.PostAsJsonAsync("/api/v1/workflows", proposal);
        Assert.That(replay.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(replay.Headers.ETag, Is.EqualTo(created.Headers.ETag));
        await using var sql = Workforce();
        Assert.That(await sql.Allocations.CountAsync(), Is.Zero, "Proposal alone must not reserve resources");
        using var conflicting = await client.PostAsJsonAsync("/api/v1/workflows", proposal with { projectId = Guid.NewGuid() });
        Assert.That(conflicting.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        using var missing = await client.PostAsync($"{location}/approve", null);
        Assert.That((int)missing.StatusCode, Is.EqualTo(428));
        using var approve = new HttpRequestMessage(HttpMethod.Post, $"{location}/approve");
        approve.Headers.IfMatch.Add(created.Headers.ETag);
        using var approved = await client.SendAsync(approve);
        Assert.That(approved.StatusCode, Is.EqualTo(HttpStatusCode.Accepted), await approved.Content.ReadAsStringAsync());
        Assert.That(approved.Headers.ETag!.Tag, Is.EqualTo("\"2\""));
        using var stale = new HttpRequestMessage(HttpMethod.Post, $"{location}/cancel");
        stale.Headers.IfMatch.Add(created.Headers.ETag);
        using var staleResponse = await client.SendAsync(stale);
        Assert.That(staleResponse.StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
        Assert.That(staleResponse.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/problem+json"));
        await FinishWorkflow(proposal.workflowId);
        using var fetched = await client.GetAsync(location);
        Assert.That(fetched.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var json = JsonDocument.Parse(await fetched.Content.ReadAsStringAsync());
        Assert.That(json.RootElement.GetProperty("state").GetString(), Is.EqualTo("Succeeded"));
        Assert.That(json.RootElement.GetProperty("transitions").GetArrayLength(), Is.EqualTo(4));
        using var invalid = await client.GetAsync("/api/v1/workflows?limit=101");
        Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task ADelayedResourceWorkerCannotTurnACommittedRejectionIntoANewReservation()
    {
        var resource = Guid.NewGuid();
        await ExecuteIdempotentAsync(new(Guid.NewGuid(), [resource], Period.Start, Period.End));
        var workflow = await ProposeAndApprove(Guid.NewGuid(), [resource], []);
        var final = await FinishWorkflow(workflow.Id);
        Assert.That(final.State, Is.EqualTo(WorkflowState.Failed));
        await using (var context = Workforce())
        {
            var blocker = await context.Allocations.SingleAsync();
            blocker.Cancel();
            await context.SaveChangesAsync();
        }
        var rejection = Assert.ThrowsAsync<WorkflowStepRejectedException>(() => WorkforceParticipant().ReserveAsync(final, default));
        Assert.That(rejection!.Code, Is.EqualTo("resource_conflict"));
        await using var sql = Workforce();
        Assert.That(await sql.Allocations.CountAsync(), Is.EqualTo(1));
        Assert.That(await sql.Allocations.CountAsync(a => !a.IsCancelled), Is.Zero);
        Assert.That(await sql.CommandReceipts.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task ADelayedLicenseWorkerCannotAllocateAfterCompensationWhenCapacityIncreases()
    {
        var (workflow, product) = await PrepareWorkflow(capacity: 1);
        var compensated = await FinishWorkflow(workflow.Id);
        Assert.That(compensated.State, Is.EqualTo(WorkflowState.Compensated));
        await using (var context = Fulfillment())
        {
            var pool = await context.Set<LicensePool>().SingleAsync(p => p.Product == product);
            pool.IncreaseCapacity(10);
            await context.SaveChangesAsync();
        }
        var rejection = Assert.ThrowsAsync<WorkflowStepRejectedException>(() => LicenseParticipant().ReserveAsync(compensated, default));
        Assert.That(rejection!.Code, Is.EqualTo("license_capacity"));
        await using var pg = Fulfillment();
        Assert.That((await pg.Set<LicensePool>().SingleAsync(p => p.Product == product)).AllocatedSeats, Is.Zero);
        Assert.That(await pg.OutboxMessages.CountAsync(m => m.Type == "license_seats_allocated"), Is.Zero);
        Assert.That(await pg.Set<WorkflowEffectReceipt>().CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task ARejectionInTheSecondLicensePoolDiscardsEveryEarlierPoolChange()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var firstProduct = $"a-{suffix}";
        var secondProduct = $"z-{suffix}";
        await using (var seed = Fulfillment())
        {
            seed.AddRange(new LicensePool(firstProduct, 10), new LicensePool(secondProduct, 0));
            await seed.SaveChangesAsync();
        }
        var workflow = await ProposeAndApprove(Guid.NewGuid(), [Guid.NewGuid()], [new(firstProduct, 3), new(secondProduct, 2)]);
        Assert.That((await FinishWorkflow(workflow.Id)).State, Is.EqualTo(WorkflowState.Compensated));
        await using var pg = Fulfillment();
        var pools = await pg.Set<LicensePool>().AsNoTracking().ToListAsync();
        Assert.That(pools.All(p => p.AllocatedSeats == 0 && p.Version == 0 && p.Allocations.Count == 0), Is.True);
        Assert.That(await pg.OutboxMessages.CountAsync(m => m.Type == "license_seats_allocated"), Is.Zero);
        Assert.That((await pg.Set<WorkflowEffectReceipt>().SingleAsync()).Succeeded, Is.False);
    }

    [Test]
    public async Task ProposalReplayAndApprovalMutationsSerializeWithoutOverwritingTheSpecification()
    {
        var proposal = ReservationWorkflow.Propose(Guid.NewGuid(), new(Guid.NewGuid(), [Guid.NewGuid()], Period, []), "proposer", DateTimeOffset.UtcNow);
        var results = await Task.WhenAll(WorkflowStore().CreateAsync(proposal, default),
            WorkflowStore().CreateAsync(ReservationWorkflow.Propose(proposal.Id, new(proposal.ProjectId, proposal.ResourceIds, proposal.Period, []),
                "proposer", DateTimeOffset.UtcNow), default));
        Assert.That(results.Select(r => r.Version), Is.EqualTo(new long[] { 1, 1 }));
        async Task<bool> Approve()
        {
            try { await WorkflowStore().MutateAsync(proposal.Id, 1, WorkflowMutation.Approve, "reviewer", true, default); return true; }
            catch (WorkflowVersionException) { return false; }
        }
        var approvals = await Task.WhenAll(Approve(), Approve());
        Assert.That(approvals.Count(a => a), Is.EqualTo(1));
        var final = await ReloadWorkflow(proposal.Id);
        Assert.That(final.Version, Is.EqualTo(2));
        Assert.That(final.SpecificationHash, Is.EqualTo(proposal.SpecificationHash));
        await using var pg = Fulfillment();
        Assert.That(await pg.Set<ReservationWorkflow>().CountAsync(), Is.EqualTo(1));
        Assert.That(await pg.OutboxMessages.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task HostedWorkflowWorkerAdvancesApprovedHttpProposalsAndCancellationWithoutManualRunnerCalls()
    {
        var product = $"hosted-{Guid.NewGuid():N}";
        await using (var seed = Fulfillment()) { seed.Add(new LicensePool(product, 5)); await seed.SaveChangesAsync(); }
        await using var factory = new ReservationFactory(_workforceOptions, _fulfillmentOptions, runWorkflowWorker: true);
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();
        using var created = await client.PostAsJsonAsync("/api/v1/workflows", new
        {
            workflowId = id, projectId = Guid.NewGuid(), resourceIds = new[] { Guid.NewGuid() },
            periodFrom = Period.Start, periodTo = Period.End, licenses = new[] { new LicenseRequirement(product, 2) }
        });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        using var approve = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workflows/{id}/approve");
        approve.Headers.IfMatch.Add(created.Headers.ETag!);
        using var approved = await client.SendAsync(approve);
        Assert.That(approved.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        var success = await WaitForState(WorkflowState.Succeeded);
        using var cancel = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workflows/{id}/cancel");
        cancel.Headers.TryAddWithoutValidation("If-Match", $"\"{success.Version}\"");
        using var cancelled = await client.SendAsync(cancel);
        Assert.That(cancelled.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        var final = await WaitForState(WorkflowState.Compensated);
        Assert.That(final.Outcome, Is.EqualTo(WorkflowOutcome.UserCancelled));
        await using var pg = Fulfillment();
        await using var sql = Workforce();
        Assert.That((await pg.Set<LicensePool>().SingleAsync(p => p.Product == product)).AllocatedSeats, Is.Zero);
        Assert.That(await sql.Allocations.CountAsync(a => !a.IsCancelled), Is.Zero);

        async Task<ReservationWorkflow> WaitForState(WorkflowState expected)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                var current = (await WorkflowStore().GetAsync(id, deadline.Token))!;
                if (current.State == expected) return current;
                Assert.That(current.State, Is.Not.EqualTo(WorkflowState.NeedsAttention));
                await Task.Delay(100, deadline.Token);
            }
        }
    }

    [Test]
    public async Task WorkflowDecisionMigrationPreservesExistingSuccessfulInboxReceipts()
    {
        await using var primary = Fulfillment();
        var connection = new NpgsqlConnectionStringBuilder(primary.Database.GetConnectionString());
        var database = $"workflow_upgrade_{Guid.NewGuid():N}";
        using var sqlBuilder = new NpgsqlCommandBuilder();
        var createDatabase = "CREATE DATABASE " + sqlBuilder.QuoteIdentifier(database);
        await primary.Database.ExecuteSqlRawAsync(createDatabase);
        connection.Database = database;
        await using var upgrade = new FulfillmentContext(new DbContextOptionsBuilder<FulfillmentContext>().UseNpgsql(connection.ConnectionString).Options);
        var migrator = upgrade.GetService<IMigrator>();
        await migrator.MigrateAsync("20261012211539_ApprovedReservationWorkflows");
        var workflowId = Guid.NewGuid();
        var hash = new string('A', 64);
        var completed = DateTimeOffset.UtcNow;
        await upgrade.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO workflow_effect_inbox (\"WorkflowId\", \"Step\", \"SpecificationHash\", \"CompletedAt\") VALUES ({workflowId}, 'reserve-licenses/v1', {hash}, {completed})");
        await migrator.MigrateAsync();
        var preserved = await upgrade.Set<WorkflowEffectReceipt>().SingleAsync();
        Assert.That(preserved.WorkflowId, Is.EqualTo(workflowId));
        Assert.That(preserved.SpecificationHash, Is.EqualTo(hash));
        Assert.That(preserved.Succeeded, Is.True);
        Assert.That(preserved.RejectionCode, Is.Null);
    }

    private sealed class LoseResourceReply(IWorkforceWorkflowParticipant inner) : IWorkforceWorkflowParticipant
    {
        public async Task<IReadOnlyList<Guid>> ReserveAsync(ReservationWorkflow workflow, CancellationToken ct)
        { await inner.ReserveAsync(workflow, ct); throw new TimeoutException("Simulated crash after resource commit"); }
        public Task ReleaseAsync(ReservationWorkflow workflow, CancellationToken ct) => inner.ReleaseAsync(workflow, ct);
    }
    private sealed class LoseLicenseReleaseReply(ILicenseWorkflowParticipant inner) : ILicenseWorkflowParticipant
    {
        public Task ReserveAsync(ReservationWorkflow workflow, CancellationToken ct) => inner.ReserveAsync(workflow, ct);
        public async Task ReleaseAsync(ReservationWorkflow workflow, CancellationToken ct)
        { await inner.ReleaseAsync(workflow, ct); throw new TimeoutException("Simulated lost compensation reply"); }
    }
    private sealed class LoseResourceReleaseReply(IWorkforceWorkflowParticipant inner) : IWorkforceWorkflowParticipant
    {
        public Task<IReadOnlyList<Guid>> ReserveAsync(ReservationWorkflow workflow, CancellationToken ct) => inner.ReserveAsync(workflow, ct);
        public async Task ReleaseAsync(ReservationWorkflow workflow, CancellationToken ct)
        { await inner.ReleaseAsync(workflow, ct); throw new TimeoutException("Simulated lost resource compensation reply"); }
    }
}
