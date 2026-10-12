using FusionOps.Domain.Events;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Domain.ValueObjects;
using FusionOps.Infrastructure.Outbox;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Domain.Tests;

public sealed class OutboxContractTests
{
    [Test]
    public void SerializationPreservesEventIdentityTimeAndStronglyTypedPayload()
    {
        var original = new ResourceAllocated(AllocationId.New(), Guid.NewGuid(), Guid.NewGuid(),
            new TimeRange(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)));
        var message = DomainEventSerializer.Serialize(original, "actor-42", "correlation-42");
        var reconstructed = DomainEventSerializer.Deserialize(message);
        Assert.That(reconstructed, Is.EqualTo(original));
        Assert.That(message.Type, Is.EqualTo("v1.ResourceAllocated"));
        Assert.That(message.Actor, Is.EqualTo("actor-42"));
    }

    [Test]
    public void UnknownContractsCannotLoadArbitraryClrTypes()
    {
        var message = new OutboxMessage { Id = Guid.NewGuid(), Type = "System.IO.File", Payload = "{}" };
        Assert.Throws<InvalidOperationException>(() => DomainEventSerializer.Deserialize(message));
    }

    [Test]
    public void RetryBackoffAndDeadLetterStateArePersistableAndBounded()
    {
        var message = new OutboxMessage();
        var now = DateTime.UtcNow;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            message.RecordFailure(new Exception(new string('x', 3000)), now, 5);
            Assert.That(message.Attempts, Is.EqualTo(attempt));
            Assert.That(message.LastError!.Length, Is.EqualTo(2000));
            if (attempt < 5) Assert.That(message.NextAttemptAt, Is.EqualTo(now.AddSeconds(Math.Pow(2, attempt))));
        }
        Assert.That(message.DeadLetteredAt, Is.EqualTo(now));
        Assert.That(message.NextAttemptAt, Is.Null);
    }

    [Test]
    public void DatabaseModelsContainOnlyTheirOwnBoundaryAndUseTheSameOutboxTable()
    {
        using var workforce = new WorkforceContext(new DbContextOptionsBuilder<WorkforceContext>().UseSqlServer("Server=localhost;Database=unused").Options);
        using var fulfillment = new FulfillmentContext(new DbContextOptionsBuilder<FulfillmentContext>().UseNpgsql("Host=localhost;Database=unused").Options);
        Assert.That(fulfillment.Model.GetEntityTypes().Any(t => t.ClrType == typeof(FusionOps.Domain.Entities.Allocation)), Is.False);
        Assert.That(workforce.Model.FindEntityType(typeof(OutboxMessage))!.GetTableName(), Is.EqualTo("Outbox"));
        Assert.That(fulfillment.Model.FindEntityType(typeof(OutboxMessage))!.GetTableName(), Is.EqualTo("Outbox"));
    }
}
