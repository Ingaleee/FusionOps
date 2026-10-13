using System;

namespace FusionOps.Domain.Events;

public record AuditEnvelope<T>(
    Guid Id,
    string EventType,
    Guid AggregateId,
    string Actor,
    string CorrelationId,
    T Payload,
    DateTime OccurredOn,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? TenantId = null
);
