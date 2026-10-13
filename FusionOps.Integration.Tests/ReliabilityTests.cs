extern alias api;
using System.Collections.Concurrent;
using EventStore.Client;
using FusionOps.Application.Abstractions;
using FusionOps.Application.Dto;
using FusionOps.Application.UseCases.AllocateResource;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Events;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Shared;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.ValueObjects;
using FusionOps.Infrastructure.Outbox;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Persistence.SqlServer;
using FusionOps.Infrastructure.Repositories;
using FusionOps.Infrastructure.Projector;
using FusionOps.Infrastructure.Persistence.Postgres.Models;
using FusionOps.Infrastructure.UnitOfWork;
using FusionOps.Presentation.Infrastructure;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using FusionOps.Infrastructure.Messaging;
using MassTransit;
using FusionOps.Infrastructure.Idempotency;
using FusionOps.Application.Pipelines;
using FusionOps.Domain.Workflows;
using FusionOps.Infrastructure.Workflows;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.DataProtection;
using System.Net.Http.Json;
using System.Net;

namespace FusionOps.Integration.Tests;

[TestFixture, Category("Database")]
public sealed partial class ReliabilityTests
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").WithPassword("FusionOps_test-2026!").Build();
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").WithDatabase("fusionops_tests").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:3-management-alpine")
        .WithUsername("fusionops").WithPassword("FusionOps_test-2026!").Build();
    private readonly IContainer _eventStore = new ContainerBuilder("eventstore/eventstore:23.10.0-bookworm-slim")
        .WithPortBinding(2113, true).WithEnvironment("EVENTSTORE_INSECURE", "true")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(2113).ForPath("/health/live")
            .ForStatusCode(System.Net.HttpStatusCode.NoContent))).Build();
    private DbContextOptions<WorkforceContext> _workforceOptions = null!;
    private DbContextOptions<FulfillmentContext> _fulfillmentOptions = null!;

    [OneTimeSetUp]
    public async Task StartDatabases()
    {
        using var startupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await Task.WhenAll(_sql.StartAsync(startupTimeout.Token), _postgres.StartAsync(startupTimeout.Token),
            _eventStore.StartAsync(startupTimeout.Token), _rabbit.StartAsync(startupTimeout.Token));
        var connection = new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "FusionOpsTests" };
        _workforceOptions = new DbContextOptionsBuilder<WorkforceContext>().UseSqlServer(connection.ConnectionString).Options;
        _fulfillmentOptions = new DbContextOptionsBuilder<FulfillmentContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var workforce = Workforce();
        await using var fulfillment = Fulfillment();
        await workforce.Database.MigrateAsync();
        await fulfillment.Database.MigrateAsync();
    }

    [SetUp]
    public async Task ClearTestData()
    {
        await using var workforce = new WorkforceContext(_workforceOptions, TenantScope.System);
        await using var fulfillment = new FulfillmentContext(_fulfillmentOptions, TenantScope.System);
        await workforce.OutboxMessages.ExecuteDeleteAsync();
        await workforce.Allocations.ExecuteDeleteAsync();
        await workforce.CommandReceipts.ExecuteDeleteAsync();
        await workforce.PlanningQuotes.ExecuteDeleteAsync();
        await workforce.ResourceCatalog.ExecuteDeleteAsync();
        await fulfillment.OutboxMessages.ExecuteDeleteAsync();
        await fulfillment.Set<WorkflowEffectReceipt>().ExecuteDeleteAsync();
        await fulfillment.Set<ReservationWorkflow>().ExecuteDeleteAsync();
        await fulfillment.Set<LicensePool>().ExecuteDeleteAsync();
        await fulfillment.AllocationHistory.ExecuteDeleteAsync();
        await fulfillment.Set<ProjectionCheckpoint>().ExecuteDeleteAsync();
        await fulfillment.StockItems.ExecuteDeleteAsync();
        await fulfillment.Set<FusionOps.Domain.Execution.ExecutionPlanDecision>().ExecuteDeleteAsync();
        await fulfillment.Set<FusionOps.Domain.Execution.ExecutionActivationDecision>().ExecuteDeleteAsync();
        await fulfillment.Set<FusionOps.Domain.Execution.ExecutionTaskFact>().ExecuteDeleteAsync();
        await fulfillment.Set<FusionOps.Domain.Execution.ExecutionTask>().ExecuteDeleteAsync();
        await fulfillment.Set<FusionOps.Domain.Execution.ExecutionActivation>().ExecuteDeleteAsync();
        await fulfillment.Set<FusionOps.Domain.Execution.TemporalLicenseEffectReceipt>().ExecuteDeleteAsync();
        await fulfillment.Set<FusionOps.Domain.Execution.ExecutionPlan>().ExecuteDeleteAsync();
        await fulfillment.Set<FusionOps.Domain.Execution.ExecutionBaseline>().ExecuteDeleteAsync();
        await fulfillment.Set<FusionOps.Domain.Scheduling.TemporalScenario>().ExecuteDeleteAsync();
        await fulfillment.Set<FusionOps.Domain.Projects.ProjectDefinitionRevision>().ExecuteDeleteAsync();
        await fulfillment.Set<FusionOps.Domain.Projects.OperationalProject>().ExecuteDeleteAsync();
    }

    [OneTimeTearDown]
    public async Task StopDatabases()
    {
        await _eventStore.DisposeAsync();
        await _rabbit.DisposeAsync();
        await _postgres.DisposeAsync();
        await _sql.DisposeAsync();
    }

    private WorkforceContext Workforce() => new(_workforceOptions, TenantScope.Legacy);
    private FulfillmentContext Fulfillment() => new(_fulfillmentOptions, TenantScope.Legacy);
    private static TimeRange Period => new(DateTimeOffset.Parse("2026-10-20T08:00:00Z"), DateTimeOffset.Parse("2026-10-20T10:00:00Z"));

    private async Task<IReadOnlyCollection<Guid>> ExecuteIdempotentAsync(AllocateCommand command, string actor = "tester",
        bool injectCommitFailure = false, Action? onHandler = null)
    {
        await EnsureCatalogAsync(command.ResourceIds);
        await using var workforce = Workforce();
        await using var fulfillment = Fulfillment();
        var execution = new TestExecutionContext(actor);
        var work = new EfUnitOfWork(workforce, fulfillment,
            injectCommitFailure ? new TestExecutionContext(new string('x', 300)) : execution);
        var receipts = new SqlCommandReceiptStore(workforce, execution, TimeProvider.System);
        var transaction = new TransactionBehavior<AllocateCommand, IReadOnlyCollection<Guid>>(work);
        var idempotency = new IdempotencyBehavior<AllocateCommand, IReadOnlyCollection<Guid>>(receipts);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await transaction.Handle(command, () => idempotency.Handle(command, () =>
        {
            onHandler?.Invoke();
            return new AllocateHandler(new AllocationRepository(workforce)).Handle(command, timeout.Token);
        }, timeout.Token), timeout.Token);
    }

    [Test]
    public async Task ConcurrentIdenticalKeysReturnTheSameResponseAndExecuteTheHandlerOnce()
    {
        var command = new AllocateCommand(Guid.NewGuid(), [Guid.NewGuid()], Period.Start, Period.End, "concurrent-key");
        var executions = 0;
        var results = await Task.WhenAll(
            ExecuteIdempotentAsync(command, onHandler: () => Interlocked.Increment(ref executions)),
            ExecuteIdempotentAsync(command, onHandler: () => Interlocked.Increment(ref executions)));
        Assert.That(results[0], Is.EqualTo(results[1]));
        Assert.That(executions, Is.EqualTo(1));
        await using var verification = Workforce();
        Assert.That(await verification.Allocations.CountAsync(), Is.EqualTo(1));
        Assert.That(await verification.OutboxMessages.CountAsync(), Is.EqualTo(1));
        Assert.That(await verification.CommandReceipts.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task ReplayInANewScopeUsesCanonicalCommandDataAndRejectsConflictingReuse()
    {
        var command = new AllocateCommand(Guid.NewGuid(), [Guid.NewGuid(), Guid.NewGuid()], Period.Start, Period.End, "restart-key");
        var first = await ExecuteIdempotentAsync(command);
        var replay = await ExecuteIdempotentAsync(command with
        {
            ResourceIds = command.ResourceIds.Reverse().ToArray(),
            PeriodFrom = command.PeriodFrom.ToOffset(TimeSpan.FromHours(3))
        }, onHandler: () => Assert.Fail("Replay must not execute the handler"));
        Assert.That(replay, Is.EqualTo(first));
        Assert.ThrowsAsync<IdempotencyConflictException>(() => ExecuteIdempotentAsync(command with { ProjectId = Guid.NewGuid() }));
        await using var verification = Workforce();
        Assert.That(await verification.Allocations.CountAsync(), Is.EqualTo(2));
        var stored = await verification.Allocations.AsNoTracking().ToListAsync();
        Assert.That(stored.All(a => a.Period == Period), Is.True);
        Assert.That(stored.Select(a => a.ResourceId), Is.EquivalentTo(command.ResourceIds));
        Assert.That(await verification.OutboxMessages.CountAsync(), Is.EqualTo(2));
        Assert.That(await verification.CommandReceipts.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task FailedCommitLeavesNoReceiptAndTheSameKeyCanBeRetried()
    {
        var command = new AllocateCommand(Guid.NewGuid(), [Guid.NewGuid()], Period.Start, Period.End, "rollback-key");
        Assert.ThrowsAsync<DbUpdateException>(() => ExecuteIdempotentAsync(command, injectCommitFailure: true));
        await using var verification = Workforce();
        Assert.That(await verification.CommandReceipts.CountAsync(), Is.Zero);
        Assert.That(await verification.Allocations.CountAsync(), Is.Zero);
        var retry = await ExecuteIdempotentAsync(command);
        Assert.That(retry.Count, Is.EqualTo(1));
        Assert.That(await verification.CommandReceipts.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task AConflictInTheSecondResourceRollsBackTheEntireBatchAndLeavesTheKeyReusable()
    {
        var resources = new[] { Guid.NewGuid(), Guid.NewGuid() }.Order().ToArray();
        var originalProject = Guid.NewGuid();
        await ExecuteIdempotentAsync(new AllocateCommand(originalProject, [resources[1]], Period.Start, Period.End));
        var batch = new AllocateCommand(Guid.NewGuid(), resources, Period.Start, Period.End, "failed-batch-key");
        Assert.ThrowsAsync<AllocationConflictException>(() => ExecuteIdempotentAsync(batch));
        await using var verification = Workforce();
        var remaining = await verification.Allocations.AsNoTracking().SingleAsync();
        Assert.That(remaining.ProjectId, Is.EqualTo(originalProject));
        Assert.That(remaining.ResourceId, Is.EqualTo(resources[1]));
        Assert.That(await verification.OutboxMessages.CountAsync(), Is.EqualTo(1));
        Assert.That(await verification.CommandReceipts.CountAsync(), Is.Zero);

        // Failed commands have no cached outcome, so the client can correct and retry.
        var retry = await ExecuteIdempotentAsync(batch with { ResourceIds = [resources[0]] });
        Assert.That(retry.Count, Is.EqualTo(1));
        Assert.That(await verification.Allocations.CountAsync(), Is.EqualTo(2));
        Assert.That(await verification.OutboxMessages.CountAsync(), Is.EqualTo(2));
        Assert.That(await verification.CommandReceipts.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task IdenticalClientKeysBelongToTheirAuthenticatedActors()
    {
        var first = new AllocateCommand(Guid.NewGuid(), [Guid.NewGuid()], Period.Start, Period.End, "shared-client-key");
        var second = first with { ProjectId = Guid.NewGuid(), ResourceIds = [Guid.NewGuid()] };
        await ExecuteIdempotentAsync(first, "actor-a");
        await ExecuteIdempotentAsync(second, "actor-b");
        await using var verification = Workforce();
        Assert.That(await verification.CommandReceipts.CountAsync(), Is.EqualTo(2));
        Assert.That(await verification.Allocations.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task AnUnrelatedKeyCanCommitWhileAnotherReceiptKeyIsLocked()
    {
        await using var heldWorkforce = Workforce();
        await using var heldFulfillment = Fulfillment();
        var execution = new TestExecutionContext();
        var heldWork = new EfUnitOfWork(heldWorkforce, heldFulfillment, execution);
        await heldWork.BeginAsync(PersistenceBoundary.Workforce);
        try
        {
            await new SqlCommandReceiptStore(heldWorkforce, execution, TimeProvider.System)
                .FindForUpdateAsync("workforce.allocate/v1", "held-key", default);
            var independent = new AllocateCommand(Guid.NewGuid(), [Guid.NewGuid()], Period.Start, Period.End, "independent-key");
            var result = await ExecuteIdempotentAsync(independent).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(result.Count, Is.EqualTo(1));
        }
        finally { await heldWork.RollbackAsync(); }
    }

    [Test]
    public async Task HttpRetryReplaysCreatedResponseAndConflictingReuseReturnsProblemDetails()
    {
        await using var factory = new ReservationFactory(_workforceOptions, _fulfillmentOptions);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "http-retry-key");
        var command = new { projectId = Guid.NewGuid(), resourceIds = new[] { Guid.NewGuid() }, periodFrom = Period.Start, periodTo = Period.End };
        await EnsureCatalogAsync(command.resourceIds);
        using var first = await client.PostAsJsonAsync("/api/v1/allocate", command);
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created), await first.Content.ReadAsStringAsync());
        var originalResponse = await first.Content.ReadAsStringAsync();
        Assert.That(first.Headers.Location?.OriginalString, Is.EqualTo($"/api/v1/projects/allocations?ids={command.projectId:D}"));
        using var lookup = await client.GetAsync(first.Headers.Location);
        Assert.That(lookup.StatusCode, Is.EqualTo(HttpStatusCode.OK), await lookup.Content.ReadAsStringAsync());
        var createdIds = (await first.Content.ReadFromJsonAsync<Guid[]>())!;
        var projects = (await lookup.Content.ReadFromJsonAsync<Dictionary<Guid, AllocationDto[]>>())!;
        Assert.That(projects[command.projectId].Single().AllocationId, Is.EqualTo(createdIds.Single()));
        Assert.That(projects[command.projectId].Single().ResourceId, Is.EqualTo(command.resourceIds.Single()));
        using var retry = await client.PostAsJsonAsync("/api/v1/allocate", command);
        Assert.That(retry.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(retry.Headers.Location, Is.EqualTo(first.Headers.Location));
        Assert.That(await retry.Content.ReadAsStringAsync(), Is.EqualTo(originalResponse));
        using var conflicting = await client.PostAsJsonAsync("/api/v1/allocate", new
        {
            projectId = Guid.NewGuid(), command.resourceIds, command.periodFrom, command.periodTo
        });
        Assert.That(conflicting.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(conflicting.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/problem+json"));
        Assert.That(await conflicting.Content.ReadAsStringAsync(), Does.Contain("idempotency_conflict"));
        await using var verification = Workforce();
        Assert.That(await verification.CommandReceipts.CountAsync(), Is.EqualTo(1));
        Assert.That(await verification.OutboxMessages.CountAsync(), Is.EqualTo(1));
        Assert.That(await verification.Allocations.CountAsync(), Is.EqualTo(1));
    }

    private sealed class ReservationFactory(DbContextOptions<WorkforceContext> workforceOptions,
        DbContextOptions<FulfillmentContext> fulfillmentOptions, bool runWorkflowWorker = false, bool runActivationWorker = false) : WebApplicationFactory<api::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development")
            .ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<WorkforceContext>();
                services.RemoveAll<FulfillmentContext>();
                services.RemoveAll<IDbContextFactory<WorkforceContext>>();
                services.RemoveAll<IDbContextFactory<FulfillmentContext>>();
                services.AddScoped(_ => new WorkforceContext(workforceOptions, TenantExecution.Current ?? TenantScope.Legacy));
                services.AddScoped(_ => new FulfillmentContext(fulfillmentOptions, TenantExecution.Current ?? TenantScope.Legacy));
                services.AddSingleton<IDbContextFactory<WorkforceContext>>(new WorkforceContextFactory(workforceOptions));
                services.AddSingleton<IDbContextFactory<FulfillmentContext>>(new TestContextFactory(fulfillmentOptions));
                if (runWorkflowWorker) services.AddHostedService<FusionOps.Presentation.BackgroundServices.ReservationWorkflowWorker>();
                if (runActivationWorker) services.AddHostedService<FusionOps.Presentation.BackgroundServices.ExecutionActivationWorker>();
            });
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DemoMode"] = "false", ["Database:Initialize"] = "false"
            }));
            return base.CreateHost(builder);
        }
    }

    private sealed class WorkforceContextFactory(DbContextOptions<WorkforceContext> options) : IDbContextFactory<WorkforceContext>
    {
        public WorkforceContext CreateDbContext() => new(options, TenantExecution.Current ?? TenantScope.Legacy);
        public Task<WorkforceContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    [Test]
    public async Task TwoConcurrentReservationsOnAnEmptyCalendarProduceOneWinnerAndOneOutboxEvent()
    {
        var resource = Guid.NewGuid();
        await EnsureCatalogAsync([resource]);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var participants = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        async Task<bool> Reserve()
        {
            await using var workforce = Workforce();
            await using var fulfillment = Fulfillment();
            var work = new EfUnitOfWork(workforce, fulfillment, new TestExecutionContext());
            await work.BeginAsync(PersistenceBoundary.Workforce, timeout.Token);
            if (Interlocked.Increment(ref participants) == 2) ready.SetResult();
            await ready.Task.WaitAsync(timeout.Token);
            try
            {
                await new AllocateHandler(new AllocationRepository(workforce)).Handle(
                    new AllocateCommand(Guid.NewGuid(), [resource], Period.Start, Period.End), timeout.Token);
                await work.CommitAsync(timeout.Token);
                return true;
            }
            catch (AllocationConflictException) { await work.RollbackAsync(); return false; }
        }
        var results = await Task.WhenAll(Reserve(), Reserve());
        Assert.That(results.Count(success => success), Is.EqualTo(1));
        await using var verification = Workforce();
        Assert.That(await verification.Allocations.CountAsync(), Is.EqualTo(1));
        Assert.That(await verification.OutboxMessages.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task FailedCommitRollsBackStateAndOutboxWithoutClearingTheOriginalEvent()
    {
        var resource = Guid.NewGuid();
        await EnsureCatalogAsync([resource]);
        await using var workforce = Workforce();
        await using var fulfillment = Fulfillment();
        var work = new EfUnitOfWork(workforce, fulfillment, new TestExecutionContext(new string('x', 300)));
        await work.BeginAsync(PersistenceBoundary.Workforce);
        var allocation = Allocation.Reserve(resource, Guid.NewGuid(), Period, []);
        workforce.Allocations.Add(allocation);
        Assert.ThrowsAsync<DbUpdateException>(() => work.CommitAsync());
        await work.RollbackAsync();
        Assert.That(allocation.DomainEvents.Count, Is.EqualTo(1));
        await using var verification = Workforce();
        Assert.That(await verification.Allocations.CountAsync(), Is.Zero);
        Assert.That(await verification.OutboxMessages.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task CrossDatabaseWritesAreRejectedBeforeEitherStoreIsSaved()
    {
        await using var workforce = Workforce();
        await using var fulfillment = Fulfillment();
        var work = new EfUnitOfWork(workforce, fulfillment, new TestExecutionContext());
        await work.BeginAsync(PersistenceBoundary.Workforce);
        workforce.Allocations.Add(Allocation.Reserve(Guid.NewGuid(), Guid.NewGuid(), Period, []));
        var product = Guid.NewGuid().ToString();
        fulfillment.Add(new LicensePool(product, 10));
        Assert.ThrowsAsync<InvalidOperationException>(() => work.CommitAsync());
        await work.RollbackAsync();
        Assert.That(await workforce.Allocations.CountAsync(), Is.Zero);
        Assert.That(await fulfillment.Set<LicensePool>().AnyAsync(p => p.Product == product), Is.False);
    }

    [Test]
    public async Task ConcurrentLicenseWritesCannotOversellTheSamePool()
    {
        var product = Guid.NewGuid().ToString("N");
        await using (var seed = Fulfillment())
        {
            seed.Add(new LicensePool(product, 10));
            await seed.SaveChangesAsync();
        }
        await using var first = Fulfillment();
        await using var second = Fulfillment();
        await using var firstWorkforce = Workforce();
        await using var secondWorkforce = Workforce();
        var firstWork = new EfUnitOfWork(firstWorkforce, first, new TestExecutionContext());
        var secondWork = new EfUnitOfWork(secondWorkforce, second, new TestExecutionContext());
        await firstWork.BeginAsync(PersistenceBoundary.Fulfillment);
        await secondWork.BeginAsync(PersistenceBoundary.Fulfillment);
        var firstPool = (await new LicenseRepository(first).FindByProductAsync(product))!;
        var secondPool = (await new LicenseRepository(second).FindByProductAsync(product))!;
        firstPool.Allocate(Guid.NewGuid(), 6);
        secondPool.Allocate(Guid.NewGuid(), 6);
        await firstWork.CommitAsync();
        Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondWork.CommitAsync());
        await secondWork.RollbackAsync();
        await using var verification = Fulfillment();
        var stored = (await new LicenseRepository(verification).FindByProductAsync(product))!;
        Assert.That(stored.AllocatedSeats, Is.EqualTo(6));
        Assert.That(stored.Allocations.Sum(a => a.Seats), Is.EqualTo(6));
        Assert.That(await verification.OutboxMessages.CountAsync(), Is.EqualTo(1));
    }

    [TestCase(true), TestCase(false)]
    public async Task PoisonMessageDoesNotBlockGoodEventsAndEventuallyDeadLetters(bool useSqlServer)
    {
        await using DbContext context = useSqlServer ? Workforce() : Fulfillment();
        var poisoned = new OutboxMessage { Id = Guid.NewGuid(), Type = "unknown.v9", Payload = "{}", OccurredOn = DateTime.UtcNow.AddSeconds(-1) };
        var domainEvent = (IDomainEvent)Allocation.Reserve(Guid.NewGuid(), Guid.NewGuid(), Period, []).DomainEvents.Single();
        var good = DomainEventSerializer.Serialize(domainEvent, "tester", "correlation-test");
        context.AddRange(poisoned, good);
        await context.SaveChangesAsync();
        var clock = new TestClock();
        var bus = new TestBus();
        var processor = new OutboxProcessor(bus, new TestAudit(), new TestPublisher(), clock, NullLogger<OutboxProcessor>.Instance);
        Assert.That(await processor.DispatchAsync(context, 10, 3, default), Is.EqualTo(1));
        Assert.That(bus.Published.Single(), Is.EqualTo(domainEvent.Id));
        for (var retry = 0; retry < 2; retry++)
        {
            clock.Advance(TimeSpan.FromMinutes(10));
            await processor.DispatchAsync(context, 10, 3, default);
        }
        var stored = await context.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Id == poisoned.Id);
        Assert.That(stored.Attempts, Is.EqualTo(3));
        Assert.That(stored.DeadLetteredAt, Is.Not.Null);
        Assert.That(bus.Published.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task ConcurrentDispatchersDoNotDeliverTheSameLockedRow()
    {
        await using (var context = Fulfillment())
        {
            context.OutboxMessages.AddRange(Enumerable.Range(0, 10).Select(_ => DomainEventSerializer.Serialize(
                Allocation.Reserve(Guid.NewGuid(), Guid.NewGuid(), Period, []).DomainEvents.Single(), "tester", "concurrent")));
            await context.SaveChangesAsync();
        }
        var bus = new TestBus();
        async Task<int> Dispatch()
        {
            await using var context = Fulfillment();
            return await new OutboxProcessor(bus, new TestAudit(), new TestPublisher(), TimeProvider.System,
                NullLogger<OutboxProcessor>.Instance).DispatchAsync(context, 10, 3, default);
        }
        await Task.WhenAll(Dispatch(), Dispatch());
        Assert.That(bus.Published.Count, Is.EqualTo(10));
        Assert.That(bus.Published.Distinct().Count(), Is.EqualTo(10));
    }

    [Test]
    public async Task AuditWriteIsIdempotentAcrossDeliveryRetries()
    {
        using var client = new EventStoreClient(EventStoreClientSettings.Create($"esdb://{_eventStore.Hostname}:{_eventStore.GetMappedPublicPort(2113)}?tls=false"));
        var writer = new EventStoreAuditWriter(client);
        var domainEvent = Allocation.Reserve(Guid.NewGuid(), Guid.NewGuid(), Period, []).DomainEvents.Single();
        var envelope = new AuditEnvelope<object>(domainEvent.Id, "v1.ResourceAllocated", Guid.NewGuid(), "tester", "retry", domainEvent, domainEvent.OccurredOn.UtcDateTime);
        await writer.WriteAsync(envelope, default);
        await writer.WriteAsync(envelope, default);
        var count = 0;
        await foreach (var item in client.ReadStreamAsync(Direction.Forwards, $"audit-event-{domainEvent.Id:N}", StreamPosition.Start)) count++;
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task LegacyAuditAppendSurvivesTenantUpgradeAndOutboxRetry()
    {
        using var client = new EventStoreClient(EventStoreClientSettings.Create($"esdb://{_eventStore.Hostname}:{_eventStore.GetMappedPublicPort(2113)}?tls=false"));
        var allocation = Allocation.Reserve(Guid.NewGuid(), Guid.NewGuid(), Period, []);
        var domainEvent = allocation.DomainEvents.Single();
        var message = DomainEventSerializer.Serialize(domainEvent, "tester", "upgrade-retry");
        // Exact pre-tenancy JSON: the old append committed, but delivery had no recorded completion.
        var oldData = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            message.Id, EventType = message.Type, AggregateId = allocation.Id.Value,
            message.Actor, message.CorrelationId, Payload = (object)domainEvent, OccurredOn = domainEvent.OccurredOn.UtcDateTime
        }, DomainEventSerializer.Options);
        await client.AppendToStreamAsync($"audit-event-{message.Id:N}", StreamState.NoStream,
            [new EventData(Uuid.FromGuid(message.Id), message.Type, oldData)]);
        await using var context = Workforce();
        context.Add(message);
        await context.SaveChangesAsync();
        var bus = new TestBus();
        var processor = new OutboxProcessor(bus, new EventStoreAuditWriter(client), new TestPublisher(), TimeProvider.System,
            NullLogger<OutboxProcessor>.Instance);
        Assert.That(await processor.DispatchAsync(context, 1, 5, default), Is.EqualTo(1));
        Assert.That(bus.Published, Has.Count.EqualTo(1));
        var matchingEvents = 0;
        await foreach (var item in client.ReadAllAsync(Direction.Forwards, Position.Start))
            if (item.Event.EventId == Uuid.FromGuid(message.Id)) matchingEvents++;
        Assert.That(matchingEvents, Is.EqualTo(1), "An upgraded retry must reuse the original stream and bytes");
        Assert.That((await context.OutboxMessages.SingleAsync()).ProcessedAt, Is.Not.Null);
    }

    private sealed record TestExecutionContext(string Actor = "tester") : IExecutionContext { public string CorrelationId => "integration-test"; }
    [Test]
    public async Task ActualRabbitDeliveryPreservesConcreteContractAndOriginalMessageIdentity()
    {
        var received = new TaskCompletionSource<(ResourceAllocated Event, Guid? MessageId, string? Tenant)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bus = Bus.Factory.CreateUsingRabbitMq(config =>
        {
            config.Host(_rabbit.Hostname, (ushort)_rabbit.GetMappedPublicPort(5672), "/", host =>
            {
                host.Username("fusionops");
                host.Password("FusionOps_test-2026!");
            });
            config.ReceiveEndpoint($"fusionops-proof-{Guid.NewGuid():N}", endpoint =>
                endpoint.Handler<ResourceAllocated>(context =>
                {
                    received.TrySetResult((context.Message, context.MessageId, context.Headers.Get<string>("fusionops-tenant-id")));
                    return Task.CompletedTask;
                }));
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await bus.StartAsync(timeout.Token);
        try
        {
            var domainEvent = (ResourceAllocated)Allocation.Reserve(Guid.NewGuid(), Guid.NewGuid(), Period, []).DomainEvents.Single();
            using var tenant = TenantExecution.Enter(TenantScope.For(TenantA));
            await new RabbitBus(bus, NullLogger<RabbitBus>.Instance).PublishAsync(domainEvent, timeout.Token);
            var delivery = await received.Task.WaitAsync(timeout.Token);
            Assert.That(delivery.Event, Is.EqualTo(domainEvent));
            Assert.That(delivery.MessageId, Is.EqualTo(domainEvent.Id));
            Assert.That(delivery.Tenant, Is.EqualTo(TenantA.ToString("D")));
        }
        finally { await bus.StopAsync(); }
    }
    [Test]
    public async Task AllocationAuditProjectsWithAReplaySafeCheckpoint()
    {
        using var client = new EventStoreClient(EventStoreClientSettings.Create($"esdb://{_eventStore.Hostname}:{_eventStore.GetMappedPublicPort(2113)}?tls=false"));
        var project = Guid.NewGuid();
        var allocation = Allocation.Reserve(Guid.NewGuid(), project, Period, []);
        var domainEvent = allocation.DomainEvents.Single();
        await new EventStoreAuditWriter(client).WriteAsync(new AuditEnvelope<object>(domainEvent.Id, "v1.ResourceAllocated",
            allocation.Id.Value, "tester", "projection-test", domainEvent, domainEvent.OccurredOn.UtcDateTime), default);
        var projector = new AuditProjector(client, new TestContextFactory(_fulfillmentOptions), NullLogger<AuditProjector>.Instance);
        await projector.ProjectBatchAsync(default);
        await projector.ProjectBatchAsync(default);
        await using var verification = Fulfillment();
        var history = await verification.AllocationHistory.SingleAsync(h => h.ProjectId == project);
        Assert.That(history.Id, Is.EqualTo(domainEvent.Id));
        Assert.That(history.AllocationId, Is.EqualTo(allocation.Id.Value));
        Assert.That(history.FromTs, Is.EqualTo(Period.Start.UtcDateTime));
        Assert.That(await verification.Set<ProjectionCheckpoint>().CountAsync(), Is.EqualTo(1));
    }

    private sealed class TestContextFactory(DbContextOptions<FulfillmentContext> options) : IDbContextFactory<FulfillmentContext>
    {
        public FulfillmentContext CreateDbContext() => new(options, TenantExecution.Current ?? TenantScope.Legacy);
        public Task<FulfillmentContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }
    private sealed class TestBus : IEventBus
    {
        public ConcurrentBag<Guid> Published { get; } = [];
        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Published.Add(domainEvent.Id);
            return Task.CompletedTask;
        }
    }
    private sealed class TestAudit : IAuditWriter
    {
        public Task WriteAsync<T>(AuditEnvelope<T> envelope, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class TestPublisher : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
    }
}
