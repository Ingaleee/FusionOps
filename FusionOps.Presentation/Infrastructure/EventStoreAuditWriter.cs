using System.Text.Json;
using FusionOps.Application.Abstractions;
using FusionOps.Domain.Events;

namespace FusionOps.Presentation.Infrastructure;

/// <summary>
/// PLACEHOLDER: EventStore audit writer implementation.
/// In this environment, EventStore client is optional; use a minimal no-op fallback if missing.
/// TODO: Implement actual EventStoreDB write logic when EventStore client is configured.
/// </summary>
public sealed class EventStoreAuditWriter : IAuditWriter
{
    public EventStoreAuditWriter()
    {
    }

    public async Task WriteAsync<T>(AuditEnvelope<T> envelope, CancellationToken ct)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(envelope, envelope.GetType(), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        // PLACEHOLDER: no-op write in this environment
        await Task.CompletedTask;
    }
}


