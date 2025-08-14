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
            var messages = await ctx.OutboxMessages
                                    .Where(m => m.ProcessedAt == null)
                                    .OrderBy(m => m.OccurredOn)
                                    .Take(50)
                                    .ToListAsync(stoppingToken);

            foreach (var msg in messages)
            {
                var evt = new OutboxDomainEvent(msg.Id, msg.OccurredOn, msg.Type, msg.Payload);
                await _bus.PublishAsync(evt);
                msg.ProcessedAt = DateTime.UtcNow;
            }

            if (messages.Count > 0)
            {
                await ctx.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("Dispatched {Count} outbox messages", messages.Count);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private record OutboxDomainEvent(Guid Id, DateTimeOffset OccurredOn, string Type, string Payload) : IDomainEvent;
}