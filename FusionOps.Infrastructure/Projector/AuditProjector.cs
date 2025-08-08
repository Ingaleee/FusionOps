using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Client;
using Microsoft.Extensions.Hosting;
using Npgsql;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FusionOps.Infrastructure.Projector;

public class AuditProjector : BackgroundService
{
    private readonly EventStoreClient _eventStore;
    private readonly string _pgConnStr;

    public AuditProjector(EventStoreClient eventStore, string pgConnStr)
    {
        _eventStore = eventStore;
        _pgConnStr = pgConnStr;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var resolvedEvent in _eventStore.ReadAllAsync(Direction.Forwards, Position.Start, cancellationToken: stoppingToken))
        {
            var type = resolvedEvent.Event.EventType;
            if (type == "v1.ResourceAllocated")
            {
                var envelope = JsonSerializer.Deserialize<AuditEnvelope<ResourceAllocatedPayload>>(resolvedEvent.Event.Data.Span, JsonOptions);
                if (envelope is null) continue;

                await using var conn = new NpgsqlConnection(_pgConnStr);
                await conn.OpenAsync(stoppingToken);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO allocation_history (id, allocation_id, project_id, resource_id, from_ts, to_ts, recorded_at, sku, qty)
                                    VALUES (@id, @allocation_id, @project_id, @resource_id, @from_ts, @to_ts, @recorded_at, @sku, @qty)
                                    ON CONFLICT(id) DO NOTHING;";
                cmd.Parameters.AddWithValue("id", envelope.Id);
                cmd.Parameters.AddWithValue("allocation_id", envelope.Payload.AllocationId);
                cmd.Parameters.AddWithValue("project_id", envelope.Payload.ProjectId);
                cmd.Parameters.AddWithValue("resource_id", envelope.Payload.ResourceId);
                cmd.Parameters.AddWithValue("from_ts", envelope.Payload.From);
                cmd.Parameters.AddWithValue("to_ts", envelope.Payload.To);
                cmd.Parameters.AddWithValue("recorded_at", envelope.OccurredOn);
                cmd.Parameters.AddWithValue("sku", envelope.Payload.Sku ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("qty", envelope.Payload.Qty);
                await cmd.ExecuteNonQueryAsync(stoppingToken);
            }
        }
    }
}

public record AuditEnvelope<T>(Guid Id, string EventType, Guid AggregateId, string Actor, string CorrelationId, T Payload, DateTime OccurredOn);

public sealed record ResourceAllocatedPayload(Guid AllocationId, Guid ResourceId, Guid ProjectId, DateTime From, DateTime To, string? Sku, int Qty);