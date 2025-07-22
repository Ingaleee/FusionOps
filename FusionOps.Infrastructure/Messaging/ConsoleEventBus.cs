using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace FusionOps.Infrastructure.Messaging;

public class ConsoleEventBus : IEventBus
{
    private readonly ILogger<ConsoleEventBus> _logger;
    public ConsoleEventBus(ILogger<ConsoleEventBus> logger) => _logger = logger;

    public Task PublishAsync(IDomainEvent domainEvent)
    {
        _logger.LogInformation("[CONSOLE BUS] Event published: {Event}", domainEvent.GetType().Name);
        return Task.CompletedTask;
    }
} 