using System.Reflection;
using System.Text.Json;
using FusionOps.Domain.Events;
using FusionOps.Domain.Events.Attributes;
using FusionOps.Domain.Shared.Interfaces;

namespace FusionOps.Infrastructure.Outbox;

/// <summary>Only explicitly known versioned contracts may be reconstructed from persisted JSON.</summary>
public static class DomainEventSerializer
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyDictionary<string, Type> Contracts = new[]
    {
        typeof(ResourceAllocated), typeof(ResourceAllocationCancelled), typeof(StockReplenished),
        typeof(ReorderPointReached), typeof(LicenseSeatsAllocated), typeof(LicenseViolationDetected),
        typeof(LicenseSeatsReleased), typeof(ReservationWorkflowChanged), typeof(CatalogResourceChanged)
    }.ToDictionary(GetName);

    public static string GetName(Type type) =>
        type.GetCustomAttribute<EventTypeAttribute>()?.Name ?? $"v1.{type.Name}";

    public static OutboxMessage Serialize(IDomainEvent domainEvent, string actor, string correlationId, Guid? tenantId = null)
    {
        var message = new OutboxMessage
        {
        Id = domainEvent.Id,
        OccurredOn = domainEvent.OccurredOn.UtcDateTime,
        Type = GetName(domainEvent.GetType()),
        Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), Options),
        Actor = actor,
        CorrelationId = correlationId
        };
        message.BindTenant(tenantId ?? FusionOps.Domain.Tenancy.TenantIdentity.Legacy);
        return message;
    }

    public static IDomainEvent Deserialize(OutboxMessage message)
    {
        if (!Contracts.TryGetValue(message.Type, out var type))
            throw new InvalidOperationException($"Unsupported event contract '{message.Type}'");
        var domainEvent = (IDomainEvent?)JsonSerializer.Deserialize(message.Payload, type, Options)
            ?? throw new InvalidOperationException("Event payload is empty");
        if (domainEvent.Id != message.Id)
            throw new InvalidOperationException("Event identity does not match its outbox record");
        return domainEvent;
    }
}
