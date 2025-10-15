using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Infrastructure.Outbox;
using FusionOps.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FusionOps.Presentation.BackgroundServices;

public class OutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventBus _bus;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(IServiceScopeFactory scopeFactory, IEventBus bus, ILogger<OutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<WorkforceContext>();
            
            await using var txn = await ctx.Database.BeginTransactionAsync(stoppingToken);
            try
            {
                var messages = await ctx.OutboxMessages
                                        .FromSqlRaw(@"
                                            SELECT TOP 50 * FROM Outbox WITH (UPDLOCK, ROWLOCK)
                                            WHERE ProcessedAt IS NULL
                                            ORDER BY OccurredOn")
                                        .ToListAsync(stoppingToken);

                foreach (var msg in messages)
                {
                    try
                    {
                        var evt = new OutboxDomainEvent(msg.Id, msg.OccurredOn, msg.Type, msg.Payload);
                        await _bus.PublishAsync(evt);
                        msg.ProcessedAt = DateTime.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to publish outbox message {MessageId}", msg.Id);
                        throw;
                    }
                }

                if (messages.Count > 0)
                {
                    await ctx.SaveChangesAsync(stoppingToken);
                    await txn.CommitAsync(stoppingToken);
                    _logger.LogInformation("Dispatched {Count} outbox messages", messages.Count);
                }
                else
                {
                    await txn.RollbackAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                await txn.RollbackAsync(stoppingToken);
                _logger.LogError(ex, "Error processing outbox messages");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private record OutboxDomainEvent(Guid Id, DateTimeOffset OccurredOn, string Type, string Payload) : IDomainEvent;
}