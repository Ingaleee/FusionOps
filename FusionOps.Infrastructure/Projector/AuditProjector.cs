using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Client;
using Microsoft.Extensions.Hosting;
using Npgsql;
using System.Text.Json;

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var resolvedEvent in _eventStore.ReadAllAsync(Direction.Forwards, Position.Start, cancellationToken: stoppingToken))
        {
            if (resolvedEvent.Event.EventType.StartsWith("v1.ResourceAllocated"))
            {
                var envelope = JsonSerializer.Deserialize<AuditEnvelope<ResourceAllocated>>(resolvedEvent.Event.Data.Span, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                if (envelope != null)
                {
                    using var conn = new NpgsqlConnection(_pgConnStr);
                    await conn.OpenAsync(stoppingToken);
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"INSERT INTO allocation_history (id, allocation_id, project_id, resource_id, from_ts, to_ts, recorded_at, sku, qty)
                        VALUES (@id, @allocation_id, @project_id, @resource_id, @from_ts, @to_ts, @recorded_at, @sku, @qty)
                        ON CONFLICT(id) DO NOTHING;";
                    cmd.Parameters.AddWithValue("id", envelope.Id);
                    cmd.Parameters.AddWithValue("allocation_id", envelope.Payload.AllocationId);
                    cmd.Parameters.AddWithValue("project_id", envelope.Payload.ProjectId);
                    cmd.Parameters.AddWithValue("resource_id", envelope.Payload.ResourceId);
                    cmd.Parameters.AddWithValue("from_ts", envelope.Payload.FromTs);
                    cmd.Parameters.AddWithValue("to_ts", envelope.Payload.ToTs);
                    cmd.Parameters.AddWithValue("recorded_at", envelope.OccurredOn);
                    cmd.Parameters.AddWithValue("sku", envelope.Payload.Sku);
                    cmd.Parameters.AddWithValue("qty", envelope.Payload.Qty);
                    await cmd.ExecuteNonQueryAsync(stoppingToken);
                }
            }
        }
    }
}