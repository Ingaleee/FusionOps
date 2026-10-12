using System.Text.Json;
using EventStore.Client;
using FusionOps.Application.Abstractions;
using FusionOps.Domain.Events;
using FusionOps.Infrastructure.Outbox;

namespace FusionOps.Presentation.Infrastructure;

/// <summary>A stable stream per event makes retries after partial delivery idempotent in the audit store.</summary>
public sealed class EventStoreAuditWriter(EventStoreClient client) : IAuditWriter
{
    public async Task WriteAsync<T>(AuditEnvelope<T> envelope, CancellationToken ct)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(envelope, DomainEventSerializer.Options);
        var stream = $"audit-event-{envelope.Id:N}";
        try
        {
            await client.AppendToStreamAsync(stream, StreamState.NoStream,
                [new EventData(Uuid.FromGuid(envelope.Id), envelope.EventType, data)], cancellationToken: ct);
        }
        catch (WrongExpectedVersionException)
        {
            var existing = client.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start, 1, cancellationToken: ct);
            await foreach (var item in existing.WithCancellation(ct))
            {
                if (item.Event.EventId == Uuid.FromGuid(envelope.Id) && item.Event.Data.Span.SequenceEqual(data)) return;
                throw new InvalidOperationException("Audit identity already exists with a different payload");
            }
            throw;
        }
    }
}
