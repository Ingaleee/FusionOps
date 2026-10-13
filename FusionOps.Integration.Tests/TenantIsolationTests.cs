using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FusionOps.Application.Abstractions;
using FusionOps.Application.Workflows;
using FusionOps.Domain.Catalog;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Enumerations;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Domain.Tenancy;
using FusionOps.Domain.ValueObjects;
using FusionOps.Domain.Workflows;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Persistence.SqlServer;
using FusionOps.Infrastructure.Repositories;
using FusionOps.Infrastructure.UnitOfWork;
using FusionOps.Infrastructure.Planning;
using FusionOps.Infrastructure.Idempotency;
using FusionOps.Application.Pipelines;
using FusionOps.Application.UseCases.AllocateResource;
using FusionOps.Infrastructure.Projector;
using FusionOps.Domain.Events;
using FusionOps.Presentation.Infrastructure;
using EventStore.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace FusionOps.Integration.Tests;

public sealed partial class ReliabilityTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private WorkforceContext TenantSql(Guid tenant) => new(_workforceOptions, TenantScope.For(tenant));
    private FulfillmentContext TenantPg(Guid tenant) => new(_fulfillmentOptions, TenantScope.For(tenant));
    private async Task<Guid> RegisterTenantResource(Guid tenant)
    {
        using var scope = TenantExecution.Enter(TenantScope.For(tenant));
        var id = Guid.NewGuid();
        await CatalogStore().RegisterAsync(id, ResourceKind.Human, new("Tenant engineer", Money.Usd(25), new Dictionary<string, int> { ["c#"] = 4 }, null), default);
        return id;
    }
    private async Task<PlanningQuote> TenantQuote(Guid tenant, Guid resource)
    {
        using var scope = TenantExecution.Enter(TenantScope.For(tenant));
        return await QuoteStore().CreateAsync(new(Guid.NewGuid(), Period, Currency.USD,
            [new(Guid.NewGuid(), "Lead", ResourceKind.Human, new Dictionary<string, int>())], [resource]), default);
    }

    [Test]
    public async Task TenantHttpCannotReadEditQuoteOrReserveAnotherTenantsResource()
    {
        var resource = await RegisterTenantResource(TenantA);
        var quote = await TenantQuote(TenantA, resource);
        await using var factory = new ReservationFactory(_workforceOptions, _fulfillmentOptions);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TenantB.ToString("D"));
        Assert.That((await client.GetAsync($"/api/v1/catalog/resources/{resource}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.GetAsync($"/api/v1/planning/quotes/{quote.Id}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        using var edit = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/catalog/resources/{resource}")
        { Content = JsonContent.Create(new { name = "Stolen", hourlyRate = 0, currency = "USD", skills = new { }, equipmentType = (string?)null, isActive = true }) };
        edit.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        Assert.That((await client.SendAsync(edit)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        using var allocation = await client.PostAsJsonAsync("/api/v1/allocate", new { projectId = Guid.NewGuid(), resourceIds = new[] { resource }, periodFrom = Period.Start, periodTo = Period.End });
        Assert.That(allocation.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        using var proposal = await client.PostAsJsonAsync("/api/v1/workflows", new { workflowId = Guid.NewGuid(), quoteId = quote.Id, licenses = Array.Empty<LicenseRequirement>() });
        Assert.That(proposal.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        await using var sql = TenantSql(TenantA);
        Assert.That((await sql.ResourceCatalog.SingleAsync()).Revision, Is.EqualTo(1));
        Assert.That(await sql.Allocations.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task SameProductAndSkuAreIndependentAndBulkRestockingIsTenantScoped()
    {
        foreach (var tenant in new[] { TenantA, TenantB })
        {
            await using var pg = TenantPg(tenant);
            pg.Add(new LicensePool("same-product", tenant == TenantA ? 5 : 99));
            pg.Add(new StockItem(StockItemId.New(), "same-sku", 10, 2, Money.Usd(1)));
            await pg.SaveChangesAsync();
        }
        await using (var pg = TenantPg(TenantA))
        {
            Assert.That((await new LicenseRepository(pg).FindByProductAsync("same-product"))!.TotalSeats, Is.EqualTo(5));
            await using var tx = await pg.Database.BeginTransactionAsync();
            await new StockRepository(pg).BulkRestockAsync([new("same-sku", 3)]);
            await pg.SaveChangesAsync();
            await tx.CommitAsync();
        }
        await using var a = TenantPg(TenantA);
        await using var b = TenantPg(TenantB);
        Assert.That((await a.StockItems.SingleAsync()).Quantity, Is.EqualTo(13));
        Assert.That((await b.StockItems.SingleAsync()).Quantity, Is.EqualTo(10));
        Assert.That((await b.Set<LicensePool>().SingleAsync()).TotalSeats, Is.EqualTo(99));
    }

    [Test, Repeat(8)]
    public async Task IdenticalActorAndIdempotencyKeysDoNotReplayAcrossTenants()
    {
        // Repeat retries the race with an empty calendar/receipt index, not the previous result.
        await ClearTestData();
        var resourceA = await RegisterTenantResource(TenantA);
        var resourceB = await RegisterTenantResource(TenantB);
        async Task<IReadOnlyCollection<Guid>> Execute(Guid tenant, Guid resource)
        {
            await using var sql = TenantSql(tenant);
            await using var pg = TenantPg(tenant);
            var execution = new TestExecutionContext("same-subject");
            var transaction = new TransactionBehavior<AllocateCommand, IReadOnlyCollection<Guid>>(new EfUnitOfWork(sql, pg, execution));
            var idempotency = new IdempotencyBehavior<AllocateCommand, IReadOnlyCollection<Guid>>(new SqlCommandReceiptStore(sql, execution, TimeProvider.System));
            var command = new AllocateCommand(Guid.NewGuid(), [resource], Period.Start, Period.End, "same-key");
            return await transaction.Handle(command, () => idempotency.Handle(command,
                () => new AllocateHandler(new AllocationRepository(sql)).Handle(command, default), default), default);
        }
        var results = await Task.WhenAll(Execute(TenantA, resourceA), Execute(TenantB, resourceB));
        Assert.That(results[0], Is.Not.EqualTo(results[1]));
        foreach (var tenant in new[] { TenantA, TenantB })
        {
            await using var sql = TenantSql(tenant);
            Assert.That(await sql.CommandReceipts.CountAsync(), Is.EqualTo(1));
            Assert.That((await sql.OutboxMessages.SingleAsync(m => m.Type == "v1.ResourceAllocated")).TenantId, Is.EqualTo(tenant));
        }
    }

    [Test]
    public async Task GlobalWorkerUsesThePersistedTenantForBothParticipantsAndCompensation()
    {
        var resource = await RegisterTenantResource(TenantA);
        var quote = await TenantQuote(TenantA, resource);
        foreach (var tenant in new[] { TenantA, TenantB })
        {
            await using var pg = TenantPg(tenant);
            pg.Add(new LicensePool("shared-name", 5));
            await pg.SaveChangesAsync();
        }
        ReservationWorkflow workflow;
        using (TenantExecution.Enter(TenantScope.For(TenantA)))
        {
            workflow = ReservationWorkflow.Propose(Guid.NewGuid(), new(quote.Evidence.Request.ProjectId, [resource], Period,
                [new("shared-name", 2)], Quote: quote.Binding), "proposer", DateTimeOffset.UtcNow, TenantA);
            await WorkflowStore().CreateAsync(workflow, default);
            await WorkflowStore().MutateAsync(workflow.Id, 1, WorkflowMutation.Approve, "reviewer", true, default);
        }
        ReservationWorkflow success;
        using (TenantExecution.Enter(TenantScope.System))
        {
            success = await FinishWorkflow(workflow.Id);
            Assert.That(success.State, Is.EqualTo(WorkflowState.Succeeded));
        }
        using (TenantExecution.Enter(TenantScope.For(TenantA)))
            await WorkflowStore().MutateAsync(workflow.Id, success.Version, WorkflowMutation.Cancel, "operator", true, default);
        using (TenantExecution.Enter(TenantScope.System))
        {
            Assert.That((await FinishWorkflow(workflow.Id)).State, Is.EqualTo(WorkflowState.Compensated));
        }
        await using var a = TenantPg(TenantA);
        await using var b = TenantPg(TenantB);
        Assert.That((await a.Set<LicensePool>().SingleAsync()).Version, Is.EqualTo(2));
        Assert.That((await b.Set<LicensePool>().SingleAsync()).Version, Is.Zero);
        Assert.That(await b.OutboxMessages.CountAsync(), Is.Zero);
        using (TenantExecution.Enter(TenantScope.For(TenantB)))
        {
            Assert.That(await WorkflowStore().GetAsync(workflow.Id, default), Is.Null);
            Assert.That(await WorkflowStore().MutateAsync(workflow.Id, 7, WorkflowMutation.Resume, "attacker", false, default), Is.Null);
        }
    }

    [Test]
    public async Task ScopedContextRejectsForeignAttachedWritesAndCannotBeReboundAfterAnUntrackedRead()
    {
        var resource = await RegisterTenantResource(TenantA);
        await using var a = TenantSql(TenantA);
        var foreign = await a.ResourceCatalog.AsNoTracking().SingleAsync(r => r.Id == resource);
        await using var b = TenantSql(TenantB);
        b.Attach(foreign);
        foreign.Revise("Foreign edit", Money.Usd(1), new Dictionary<string, int>(), null, false);
        Assert.ThrowsAsync<TenantBoundaryException>(() => b.SaveChangesAsync());
        Assert.Throws<InvalidOperationException>(() => a.BindScope(TenantScope.For(TenantB)));
        await using var missing = new WorkforceContext(_workforceOptions);
        Assert.ThrowsAsync<TenantRequiredException>(() => missing.ResourceCatalog.ToListAsync());
    }

    [Test]
    public async Task SqlRowPolicyFiltersRawReadsAndBlocksForeignWritesAndCompositeOwnership()
    {
        var resource = await RegisterTenantResource(TenantA);
        await using var b = TenantSql(TenantB);
        Assert.That(await b.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM ResourceCatalog WHERE Id = {resource}").SingleAsync(), Is.Zero);
        Assert.That(await b.ResourceCatalog.IgnoreQueryFilters().CountAsync(), Is.Zero);
        Assert.ThrowsAsync<SqlException>(() => b.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ResourceCatalog (Id, TenantId, Name, Kind, IsActive, Revision, HourRateAmount, HourRateCurrency) VALUES ({Guid.NewGuid()}, {TenantA}, 'Foreign', 'Human', 1, 1, 25, 1)"));
        var fk = Assert.ThrowsAsync<SqlException>(() => b.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Allocations (Id, TenantId, ResourceId, ProjectId, PeriodStart, PeriodEnd, IsCancelled) VALUES ({Guid.NewGuid()}, {TenantB}, {resource}, {Guid.NewGuid()}, {Period.Start}, {Period.End}, 0)"));
        Assert.That(fk!.Number, Is.EqualTo(547));
    }

    [Test]
    public async Task PostgresRowPolicyProtectsRawAndOwnedReadsUnderANonBypassRuntimeRole()
    {
        await using (var a = TenantPg(TenantA)) { var pool = new LicensePool("private-product", 5); pool.Allocate(Guid.NewGuid(), 2); a.Add(pool); await a.SaveChangesAsync(); }
        await using (var admin = new FulfillmentContext(_fulfillmentOptions, TenantScope.System))
        {
            await admin.Database.ExecuteSqlRawAsync("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fusionops_app_test') THEN CREATE ROLE fusionops_app_test LOGIN PASSWORD 'FusionOps_test-2026!' NOSUPERUSER NOBYPASSRLS; END IF; END $$; GRANT USAGE ON SCHEMA public TO fusionops_app_test; GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO fusionops_app_test; GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO fusionops_app_test;");
        }
        var connection = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Username = "fusionops_app_test", Password = "FusionOps_test-2026!", MaxPoolSize = 1 };
        var options = new DbContextOptionsBuilder<FulfillmentContext>().UseNpgsql(connection.ConnectionString).Options;
        await using (var b = new FulfillmentContext(options, TenantScope.For(TenantB)))
        {
            Assert.That(await b.Set<LicensePool>().IgnoreQueryFilters().CountAsync(), Is.Zero);
            Assert.That(await b.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM license_pools").SingleAsync(), Is.Zero);
            Assert.That(await b.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM license_allocations").SingleAsync(), Is.Zero);
            await b.Database.ExecuteSqlRawAsync("SELECT set_config('fusionops.system', 'on', false)");
            Assert.That(await b.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM license_pools").SingleAsync(), Is.Zero, "App role cannot turn on global access");
            Assert.That(Assert.ThrowsAsync<PostgresException>(() => b.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO license_pools (\"Id\", \"TenantId\", \"Product\", \"TotalSeats\", \"AllocatedSeats\", \"Version\") VALUES ({Guid.NewGuid()}, {TenantA}, 'foreign', 5, 0, 0)"))!.SqlState, Is.EqualTo("42501"));
        }
        await using (var a = new FulfillmentContext(options, TenantScope.For(TenantA)))
            Assert.That(await a.Set<LicensePool>().IgnoreQueryFilters().CountAsync(), Is.EqualTo(1), "Reused physical connections receive the new scope");
        await using (var unset = new NpgsqlConnection(connection.ConnectionString))
        {
            await unset.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT COUNT(*)::int FROM license_pools", unset);
            Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(0), "A pooled connection cannot inherit another request's scope");
        }
    }

    [Test]
    public async Task TenantAuditStreamsProjectToTheCorrectTenantAndReplayWithoutLeakingHistory()
    {
        using var client = new EventStoreClient(EventStoreClientSettings.Create($"esdb://{_eventStore.Hostname}:{_eventStore.GetMappedPublicPort(2113)}?tls=false"));
        var project = Guid.NewGuid();
        foreach (var tenant in new[] { TenantA, TenantB })
        {
            var allocation = Allocation.Reserve(Guid.NewGuid(), project, Period, []);
            var domainEvent = allocation.DomainEvents.Single();
            await new EventStoreAuditWriter(client).WriteAsync(new AuditEnvelope<object>(domainEvent.Id, "v1.ResourceAllocated", allocation.Id.Value,
                "same-actor", "tenant-audit", domainEvent, domainEvent.OccurredOn.UtcDateTime, tenant), default);
        }
        var projector = new AuditProjector(client, new TestContextFactory(_fulfillmentOptions), NullLogger<AuditProjector>.Instance);
        await projector.ProjectBatchAsync(default);
        await projector.ProjectBatchAsync(default);
        foreach (var tenant in new[] { TenantA, TenantB })
        {
            await using var pg = TenantPg(tenant);
            Assert.That(await pg.AllocationHistory.CountAsync(r => r.ProjectId == project), Is.EqualTo(1));
            Assert.That((await pg.AllocationHistory.SingleAsync(r => r.ProjectId == project)).TenantId, Is.EqualTo(tenant));
        }
    }

    [Test]
    public async Task SqlApplicationPrincipalCannotAcquireGlobalAccessBySettingSessionFlags()
    {
        var resource = await RegisterTenantResource(TenantA);
        await using (var server = new SqlConnection(_sql.GetConnectionString()))
        {
            await server.OpenAsync();
            await using var create = new SqlCommand("IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'fusionops_app_test') CREATE LOGIN fusionops_app_test WITH PASSWORD='FusionOps_test-2026!', CHECK_POLICY=OFF;", server);
            await create.ExecuteNonQueryAsync();
        }
        await using (var admin = new WorkforceContext(_workforceOptions, TenantScope.System))
            await admin.Database.ExecuteSqlRawAsync("IF DATABASE_PRINCIPAL_ID(N'fusionops_app_test') IS NULL CREATE USER fusionops_app_test FOR LOGIN fusionops_app_test; ALTER ROLE db_datareader ADD MEMBER fusionops_app_test; ALTER ROLE db_datawriter ADD MEMBER fusionops_app_test;");
        await using var metadata = Workforce();
        var connection = new SqlConnectionStringBuilder(metadata.Database.GetConnectionString())
        { UserID = "fusionops_app_test", Password = "FusionOps_test-2026!", MaxPoolSize = 1 };
        var options = new DbContextOptionsBuilder<WorkforceContext>().UseSqlServer(connection.ConnectionString).Options;
        await using (var a = new WorkforceContext(options, TenantScope.For(TenantA)))
            Assert.That(await a.ResourceCatalog.IgnoreQueryFilters().CountAsync(), Is.EqualTo(1));
        await using (var b = new WorkforceContext(options, TenantScope.For(TenantB)))
            Assert.That(await b.ResourceCatalog.IgnoreQueryFilters().CountAsync(), Is.Zero);
        await using var raw = new SqlConnection(connection.ConnectionString);
        await raw.OpenAsync();
        await using var flags = new SqlCommand("EXEC sys.sp_set_session_context @key=N'fusionops.tenant', @value=@tenant; EXEC sys.sp_set_session_context @key=N'fusionops.system', @value=N'on'; SELECT COUNT(*) FROM ResourceCatalog;", raw);
        flags.Parameters.AddWithValue("tenant", TenantB.ToString("D"));
        Assert.That(await flags.ExecuteScalarAsync(), Is.EqualTo(0), "Application credentials cannot enable worker authority");
    }
}
