using System.Text.Json;
using FusionOps.Application.Abstractions;
using FusionOps.Domain.Events;

namespace FusionOps.Presentation.Infrastructure;

public sealed class EventStoreAuditWriter : IAuditWriter
{
    // In this environment, EventStore client is optional; use a minimal no-op fallback if missing
    private readonly object? _client;

    public EventStoreAuditWriter()
    {
        _client = null;
    }

    public async Task WriteAsync<T>(AuditEnvelope<T> envelope, CancellationToken ct)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(envelope, envelope.GetType(), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        // no-op write in this environment
        await Task.CompletedTask;
    }
}


