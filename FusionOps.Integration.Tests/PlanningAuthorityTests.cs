using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FusionOps.Application.Planning;
using FusionOps.Domain.Catalog;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Enumerations;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.ValueObjects;
using FusionOps.Domain.Workflows;
using FusionOps.Application.Workflows;
using FusionOps.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using FusionOps.Infrastructure.Persistence.SqlServer;
using Microsoft.Data.SqlClient;

namespace FusionOps.Integration.Tests;

public sealed partial class ReliabilityTests
{
    private SqlResourceCatalogStore CatalogStore() => new(new WorkforceContextFactory(_workforceOptions), new TestExecutionContext());
    private SqlPlanningQuoteStore QuoteStore() => new(new WorkforceContextFactory(_workforceOptions), new TestExecutionContext(), new ResourcePlanner());
    private async Task EnsureCatalogAsync(IEnumerable<Guid> ids)
    {
        await using var context = Workforce();
        await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        foreach (var id in ids.Distinct().Order())
            if (await CatalogLocks.FindAsync(context, id, default) is null)
                context.Add(CatalogResource.Register(id, $"Resource {id:N}", ResourceKind.Human, Money.Usd(25), new Dictionary<string, int> { ["c#"] = 4 }));
        await context.SaveChangesAsync();
        await tx.CommitAsync();
    }
    private async Task<PlanningQuote> CreateQuoteAsync(Guid? resourceId = null, int validity = 300, TimeRange? period = null)
    {
        var id = resourceId ?? Guid.NewGuid();
        await EnsureCatalogAsync([id]);
        return await QuoteStore().CreateAsync(new(Guid.NewGuid(), period ?? Period, Currency.USD,
            [new(Guid.NewGuid(), "Backend architect", ResourceKind.Human, new Dictionary<string, int> { ["c#"] = 4 })], [id], validity), default);
    }
    private async Task<ReservationWorkflow> ApproveQuoteAsync(PlanningQuote quote)
    {
        var evidence = quote.Evidence;
        var proposal = ReservationWorkflow.Propose(Guid.NewGuid(), new(evidence.Request.ProjectId,
            evidence.Result.Assignments.Select(a => a.ResourceId).ToArray(), evidence.Request.Period, [], Quote: quote.Binding), "proposer", DateTimeOffset.UtcNow);
        await WorkflowStore().CreateAsync(proposal, default);
        return (await WorkflowStore().MutateAsync(proposal.Id, 1, WorkflowMutation.Approve, "reviewer", true, default))!;
    }

    [Test]
    public async Task QuotePersistsAuthoritativeSkillsPriceAndExactDecimalCostAcrossBothStores()
    {
        var resource = Guid.NewGuid();
        await CatalogStore().RegisterAsync(resource, ResourceKind.Human, new("Architect", Money.Usd(123.456789m), new Dictionary<string, int> { ["c#"] = 5 }, null), default);
        var period = new TimeRange(Period.Start, Period.Start.AddTicks(12345678900));
        var quote = await CreateQuoteAsync(resource, period: period);
        var reloaded = (await QuoteStore().GetAsync(quote.Id, default))!;
        Assert.That(reloaded.Evidence.Candidates.Single().Resource.HourRate, Is.EqualTo(Money.Usd(123.456789m)));
        Assert.That(reloaded.Evidence.Candidates.Single().Resource.Skills["c#"], Is.EqualTo(5));
        Assert.That(reloaded.Evidence.Result.Objective, Is.EqualTo(reloaded.Evidence.Result.DualObjective));
        var workflow = await ApproveQuoteAsync(quote);
        var final = await FinishWorkflow(workflow.Id);
        Assert.That(final.State, Is.EqualTo(WorkflowState.Succeeded), $"{final.FailureCode} / {final.LastErrorCode}");
        Assert.That(final.Quote, Is.EqualTo(quote.Binding), "Fractional-hour prices must survive PostgreSQL without rounding");
        Assert.That((await QuoteStore().GetAsync(quote.Id, default))!.ConsumedByWorkflowId, Is.EqualTo(final.Id));
    }

    [Test]
    public async Task UnknownResourceIsRejectedByHttpAndCannotCreateAnAllocationOrReceipt()
    {
        await using var factory = new ReservationFactory(_workforceOptions, _fulfillmentOptions);
        using var client = factory.CreateClient();
        using var result = await client.PostAsJsonAsync("/api/v1/allocate", new { projectId = Guid.NewGuid(), resourceIds = new[] { Guid.NewGuid() }, periodFrom = Period.Start, periodTo = Period.End });
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), await result.Content.ReadAsStringAsync());
        Assert.That(await result.Content.ReadAsStringAsync(), Does.Contain("resource_unknown"));
        await using var sql = Workforce();
        Assert.That(await sql.Allocations.CountAsync(), Is.Zero);
        Assert.That(await sql.CommandReceipts.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task MetadataChangeRejectsTheWholeQuoteAndTheDecisionRemainsDurableAfterRevertingTheRate()
    {
        var resources = new[] { Guid.NewGuid(), Guid.NewGuid() }.Order().ToArray();
        await EnsureCatalogAsync(resources);
        var quote = await QuoteStore().CreateAsync(new(Guid.NewGuid(), Period, Currency.USD,
            resources.Select((_, i) => new DemandSlot(Guid.NewGuid(), $"Engineer {i}", ResourceKind.Human, new Dictionary<string, int>())).ToArray(), resources), default);
        var workflow = await ApproveQuoteAsync(quote);
        await CatalogStore().ReviseAsync(resources[1], 1, new("Changed rate", Money.Usd(99), new Dictionary<string, int>(), null), default);
        var failed = await FinishWorkflow(workflow.Id);
        Assert.That(failed.FailureCode, Is.EqualTo("catalog_changed"));
        await CatalogStore().ReviseAsync(resources[1], 2, new("Restored rate", Money.Usd(25), new Dictionary<string, int>(), null), default);
        var replay = Assert.ThrowsAsync<WorkflowStepRejectedException>(() => WorkforceParticipant().ReserveAsync(failed, default));
        Assert.That(replay!.Code, Is.EqualTo("catalog_changed"));
        await using var sql = Workforce();
        Assert.That(await sql.Allocations.CountAsync(), Is.Zero);
        Assert.That(await sql.CommandReceipts.CountAsync(), Is.EqualTo(1));
        Assert.That((await QuoteStore().GetAsync(quote.Id, default))!.ConsumedByWorkflowId, Is.Null);
    }

    [Test]
    public async Task OneQuoteCanBeConsumedByOnlyOneWorkflowAndItsReceiptSurvivesExpiryAndCancellation()
    {
        var quote = await CreateQuoteAsync();
        var first = await ApproveQuoteAsync(quote);
        var second = await ApproveQuoteAsync(quote);
        async Task<bool> Reserve(ReservationWorkflow w)
        {
            try { await WorkforceParticipant().ReserveAsync(w, default); return true; }
            catch (WorkflowStepRejectedException) { return false; }
        }
        var results = await Task.WhenAll(Reserve(first), Reserve(second));
        Assert.That(results.Count(r => r), Is.EqualTo(1));
        var winner = results[0] ? first : second;
        var original = await WorkforceParticipant().ReserveAsync(winner, default);
        await using (var sql = Workforce())
            await sql.Database.ExecuteSqlInterpolatedAsync($"UPDATE PlanningQuotes SET ExpiresAt = DATEADD(second, -1, SYSDATETIMEOFFSET()) WHERE Id = {quote.Id}");
        Assert.That(await WorkforceParticipant().ReserveAsync(winner, default), Is.EqualTo(original), "Committed receipts take precedence over changed quote state");
        // Advance the persisted coordinator from its replay, cancel, then replay the old effect.
        await FinishWorkflow(winner.Id);
        var current = await ReloadWorkflow(winner.Id);
        await WorkflowStore().MutateAsync(winner.Id, current.Version, WorkflowMutation.Cancel, "operator", true, default);
        var final = await FinishWorkflow(winner.Id);
        Assert.That(final.State, Is.EqualTo(WorkflowState.Compensated));
        Assert.That(await WorkforceParticipant().ReserveAsync(final, default), Is.EqualTo(original));
        await using var verify = Workforce();
        Assert.That(await verify.Allocations.CountAsync(a => !a.IsCancelled), Is.Zero);
        Assert.That(await verify.Allocations.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task DeactivationBlocksNewBookingsButDoesNotBlockCompensation()
    {
        var quote = await CreateQuoteAsync();
        var workflow = await ApproveQuoteAsync(quote);
        var success = await FinishWorkflow(workflow.Id);
        var resource = success.ResourceIds.Single();
        await CatalogStore().ReviseAsync(resource, 1, new("Deactivated", Money.Usd(25), new Dictionary<string, int>(), null, false), default);
        await WorkflowStore().MutateAsync(workflow.Id, success.Version, WorkflowMutation.Cancel, "operator", true, default);
        Assert.That((await FinishWorkflow(workflow.Id)).State, Is.EqualTo(WorkflowState.Compensated));
        await using var factory = new ReservationFactory(_workforceOptions, _fulfillmentOptions);
        using var client = factory.CreateClient();
        using var result = await client.PostAsJsonAsync("/api/v1/allocate", new { projectId = Guid.NewGuid(), resourceIds = new[] { resource }, periodFrom = Period.Start, periodTo = Period.End });
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await result.Content.ReadAsStringAsync(), Does.Contain("resource_inactive"));
    }

    [Test]
    public async Task ABookingAfterQuoteCreationRejectsTheEntireReservationWithoutConsumingTheQuote()
    {
        var quote = await CreateQuoteAsync();
        var resource = quote.Evidence.Result.Assignments.Single().ResourceId;
        await ExecuteIdempotentAsync(new(Guid.NewGuid(), [resource], Period.Start, Period.End));
        var workflow = await ApproveQuoteAsync(quote);
        var failed = await FinishWorkflow(workflow.Id);
        Assert.That(failed.FailureCode, Is.EqualTo("resource_conflict"));
        Assert.That((await QuoteStore().GetAsync(quote.Id, default))!.ConsumedByWorkflowId, Is.Null);
        await using var sql = Workforce();
        Assert.That(await sql.Allocations.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task ExpiredQuoteRejectionRollsBackEveryStagedAllocation()
    {
        var source = await CreateQuoteAsync();
        var quote = PlanningQuote.Create(source.Evidence, "tester", DateTimeOffset.UtcNow.AddMinutes(-1), 15);
        await using (var sql = Workforce()) { sql.PlanningQuotes.Add(quote); await sql.SaveChangesAsync(); }
        var workflow = await ApproveQuoteAsync(quote);
        var failed = await FinishWorkflow(workflow.Id);
        Assert.That(failed.FailureCode, Is.EqualTo("quote_expired"));
        await using var verify = Workforce();
        Assert.That(await verify.Allocations.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task CatalogHttpPreconditionsAndQuotesIgnoreCallerSuppliedEconomicOverrides()
    {
        await using var factory = new ReservationFactory(_workforceOptions, _fulfillmentOptions);
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();
        using var created = await client.PostAsJsonAsync("/api/v1/catalog/resources", new { id, name = "Architect", kind = "Human", hourlyRate = 40, currency = "USD", skills = new { csharp = 5 } });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        using var quoted = await client.PostAsJsonAsync("/api/v1/planning/quotes", new
        {
            projectId = Guid.NewGuid(), from = Period.Start, to = Period.End, currency = "USD", candidateIds = new[] { id },
            slots = new[] { new { id = Guid.NewGuid(), name = "Lead", kind = "Human", requiredSkills = new { csharp = 5 } } },
            candidates = new[] { new { id, hourlyRate = 0, skills = new { csharp = 0 } } }
        });
        Assert.That(quoted.StatusCode, Is.EqualTo(HttpStatusCode.Created), await quoted.Content.ReadAsStringAsync());
        using var evidence = JsonDocument.Parse(await quoted.Content.ReadAsStringAsync());
        Assert.That(evidence.RootElement.GetProperty("evidence").GetProperty("result").GetProperty("totalCost").GetProperty("amount").GetDecimal(), Is.EqualTo(80));
        using var proposal = await client.PostAsJsonAsync("/api/v1/workflows", new
        {
            workflowId = Guid.NewGuid(), quoteId = evidence.RootElement.GetProperty("id").GetGuid(), licenses = Array.Empty<LicenseRequirement>(),
            projectId = Guid.NewGuid(), resourceIds = new[] { Guid.NewGuid() }, periodFrom = Period.Start.AddDays(1), hourlyRate = 0
        });
        Assert.That(proposal.StatusCode, Is.EqualTo(HttpStatusCode.Created), await proposal.Content.ReadAsStringAsync());
        using var proposed = JsonDocument.Parse(await proposal.Content.ReadAsStringAsync());
        Assert.That(proposed.RootElement.GetProperty("projectId").GetGuid(), Is.EqualTo(evidence.RootElement.GetProperty("evidence").GetProperty("request").GetProperty("projectId").GetGuid()));
        Assert.That(proposed.RootElement.GetProperty("resourceIds")[0].GetGuid(), Is.EqualTo(id));
        Assert.That(proposed.RootElement.GetProperty("quote").GetProperty("amount").GetDecimal(), Is.EqualTo(80));
        var edit = new { name = "Updated", hourlyRate = 45, currency = "USD", skills = new { csharp = 4 }, equipmentType = (string?)null, isActive = true };
        using var missing = await client.PutAsJsonAsync(created.Headers.Location, edit);
        Assert.That((int)missing.StatusCode, Is.EqualTo(428));
        using var request = new HttpRequestMessage(HttpMethod.Put, created.Headers.Location) { Content = JsonContent.Create(edit) };
        request.Headers.IfMatch.Add(created.Headers.ETag!);
        using var updated = await client.SendAsync(request);
        Assert.That(updated.StatusCode, Is.EqualTo(HttpStatusCode.OK), await updated.Content.ReadAsStringAsync());
        using var stale = new HttpRequestMessage(HttpMethod.Put, created.Headers.Location) { Content = JsonContent.Create(edit) };
        stale.Headers.IfMatch.Add(created.Headers.ETag!);
        using var conflict = await client.SendAsync(stale);
        Assert.That(conflict.StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
    }

    [Test]
    public async Task ConcurrentCatalogEditAndAcceptanceEitherPreserveTheReviewedPriceOrDurablyRejectIt()
    {
        var quote = await CreateQuoteAsync();
        var workflow = await ApproveQuoteAsync(quote);
        var resource = workflow.ResourceIds.Single();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Reserve()
        {
            await start.Task;
            try { await WorkforceParticipant().ReserveAsync(workflow, default); return true; }
            catch (WorkflowStepRejectedException error) { Assert.That(error.Code, Is.EqualTo("catalog_changed")); return false; }
        }
        async Task Edit()
        {
            await start.Task;
            await CatalogStore().ReviseAsync(resource, 1, new("New rate", Money.Usd(99), new Dictionary<string, int> { ["c#"] = 5 }, null), default);
        }
        var reservation = Reserve();
        var edit = Edit();
        start.SetResult();
        await Task.WhenAll(reservation, edit);
        await using var sql = Workforce();
        Assert.That(await sql.Allocations.CountAsync(), Is.EqualTo(await reservation ? 1 : 0));
        Assert.That(await sql.CommandReceipts.CountAsync(), Is.EqualTo(1));
        Assert.That((await CatalogStore().GetAsync(resource, default))!.Revision, Is.EqualTo(2));
        Assert.That((await ReloadWorkflow(workflow.Id)).Quote!.TotalCost, Is.EqualTo(Money.Usd(50)));
        Assert.That((await QuoteStore().GetAsync(quote.Id, default))!.ConsumedByWorkflowId, Is.EqualTo(await reservation ? workflow.Id : (Guid?)null));
    }

    [Test]
    public async Task QuoteUsesActualHalfOpenCalendarsAndAnIncompleteQuoteCannotStartAWorkflow()
    {
        var resource = Guid.NewGuid();
        await ExecuteIdempotentAsync(new(Guid.NewGuid(), [resource], Period.Start, Period.End));
        var blocked = await CreateQuoteAsync(resource);
        Assert.That(blocked.Evidence.Result.IsComplete, Is.False);
        Assert.That(blocked.Evidence.Result.Rejections.Single().Reason, Does.Contain("already booked"));
        var adjacent = await CreateQuoteAsync(resource, period: new(Period.End, Period.End.AddHours(1)));
        Assert.That(adjacent.Evidence.Result.IsComplete, Is.True);
        await using var factory = new ReservationFactory(_workforceOptions, _fulfillmentOptions);
        using var client = factory.CreateClient();
        using var result = await client.PostAsJsonAsync("/api/v1/workflows", new { workflowId = Guid.NewGuid(), quoteId = blocked.Id, licenses = Array.Empty<LicenseRequirement>() });
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await result.Content.ReadAsStringAsync(), Does.Contain("quote_incomplete"));
        await using var pg = Fulfillment();
        Assert.That(await pg.Set<ReservationWorkflow>().CountAsync(), Is.Zero);
    }

    [Test]
    public async Task CatalogUpgradePreservesHistoricalBookingsAndRequiresReviewBeforeNewAdmission()
    {
        // This database belongs exclusively to this test container and disappears with it.
        var connection = new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = $"CatalogUpgrade_{Guid.NewGuid():N}" };
        await using var upgrade = new WorkforceContext(new DbContextOptionsBuilder<WorkforceContext>().UseSqlServer(connection.ConnectionString).Options, FusionOps.Application.Abstractions.TenantScope.Legacy);
        var migrator = upgrade.GetService<IMigrator>();
        await migrator.MigrateAsync("20261012202310_DurableCommandReceipts");
        var human = Guid.NewGuid();
        var orphan = Guid.NewGuid();
        var allocation = Guid.NewGuid();
        var project = Guid.NewGuid();
        await upgrade.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO HumanResources (Id, FullName, HourRateAmount, HourRateCurrency) VALUES ({human}, 'Legacy architect', 123, 1)");
        await upgrade.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO EquipmentResources (Id, Model, Type, HourRateAmount, HourRateCurrency) VALUES ({human}, 'Conflicting equipment', 1, 10, 1)");
        Assert.That(Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync())!.Number, Is.EqualTo(51000));
        Assert.That(await upgrade.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sys.tables WHERE name = 'ResourceCatalog'").SingleAsync(), Is.Zero, "Collision must roll back the migration");
        await upgrade.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM EquipmentResources WHERE Id = {human}");
        await upgrade.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Allocations (Id, ResourceId, ProjectId, PeriodStart, PeriodEnd, IsCancelled) VALUES ({allocation}, {orphan}, {project}, {Period.Start}, {Period.End}, 0)");
        await migrator.MigrateAsync();
        var resources = await upgrade.ResourceCatalog.AsNoTracking().Include(r => r.Skills).ToListAsync();
        Assert.That(resources.Count, Is.EqualTo(2));
        Assert.That(resources.All(r => !r.IsActive && r.Revision == 1 && r.Skills.Count == 0), Is.True);
        Assert.That(resources.Single(r => r.Id == human).HourRate, Is.EqualTo(Money.Usd(123)));
        var preserved = await upgrade.Allocations.AsNoTracking().SingleAsync();
        Assert.That(preserved.Id.Value, Is.EqualTo(allocation));
        Assert.That(preserved.Period, Is.EqualTo(Period));
        await using (var tx = await upgrade.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            Assert.That(Assert.ThrowsAsync<ResourceAdmissionException>(() => CatalogLocks.RequireAsync(upgrade, orphan, true, default))!.Code, Is.EqualTo("resource_inactive"));
            Assert.That((await CatalogLocks.RequireAsync(upgrade, orphan, false, default)).Id, Is.EqualTo(orphan));
            await tx.RollbackAsync();
        }
        var unknown = Guid.NewGuid();
        var violation = Assert.ThrowsAsync<SqlException>(() => upgrade.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Allocations (Id, ResourceId, ProjectId, PeriodStart, PeriodEnd, IsCancelled) VALUES ({Guid.NewGuid()}, {unknown}, {project}, {Period.Start}, {Period.End}, 0)"));
        Assert.That(violation!.Number, Is.EqualTo(547), "The database foreign key protects catalog identity");
    }

    [Test]
    public async Task ChangedQuoteEvidenceIsRejectedBeforeEffectsAndRetainsADurableDecision()
    {
        var quote = await CreateQuoteAsync();
        var workflow = await ApproveQuoteAsync(quote);
        await using (var sql = Workforce())
            await sql.Database.ExecuteSqlInterpolatedAsync($"UPDATE PlanningQuotes SET EvidenceJson = '{{}}' WHERE Id = {quote.Id}");
        var failed = await FinishWorkflow(workflow.Id);
        Assert.That(failed.FailureCode, Is.EqualTo("quote_evidence_invalid"));
        var replay = Assert.ThrowsAsync<WorkflowStepRejectedException>(() => WorkforceParticipant().ReserveAsync(failed, default));
        Assert.That(replay!.Code, Is.EqualTo("quote_evidence_invalid"));
        Assert.That(Assert.ThrowsAsync<QuoteAdmissionException>(() => QuoteStore().GetAsync(quote.Id, default))!.Code, Is.EqualTo("quote_evidence_invalid"));
        await using var verify = Workforce();
        Assert.That(await verify.Allocations.CountAsync(), Is.Zero);
        Assert.That(await verify.CommandReceipts.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task SubMicrosecondQuoteBoundariesAreRejectedInsteadOfSilentlyChangingTheBookedPeriod()
    {
        var resource = Guid.NewGuid();
        await EnsureCatalogAsync([resource]);
        var error = Assert.ThrowsAsync<FusionOps.Domain.Shared.DomainException>(() => CreateQuoteAsync(resource, period: new(Period.Start, Period.End.AddTicks(1))));
        Assert.That(error!.Message, Does.Contain("microsecond"));
        await using var sql = Workforce();
        Assert.That(await sql.PlanningQuotes.CountAsync(), Is.Zero);
    }
}
