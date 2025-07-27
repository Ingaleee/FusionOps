using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FusionOps.Infrastructure.Messaging;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

public class ChaosRabbitTests : IAsyncLifetime
{
    private readonly TestcontainersContainer _rabbit;
    private ServiceProvider _provider = null!;

    public ChaosRabbitTests()
    {
        _rabbit = new TestcontainersBuilder<TestcontainersContainer>()
            .WithImage("rabbitmq:3-management")
            .WithPortBinding(5672, true)
            .WithPortBinding(15672, true)
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _rabbit.StartAsync();
        var host = _rabbit.Hostname;
        var port = _rabbit.GetMappedPublicPort(5672);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole());
        services.AddMassTransit(x =>
        {
            x.UsingRabbitMq((ctx, cfg) => cfg.Host(host, port, "/", h => { }));
        });
        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null) await _provider.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    [Fact(Skip = "Disabled due to Docker/Testcontainers issue on this machine")]
    public async Task RabbitBus_ShouldRetry_OnFailure()
    {
        var bus = _provider.GetRequiredService<RabbitBus>();
        var domEvent = new TestDomainEvent();

        // Stop container to induce failure
        await _rabbit.StopAsync();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<Exception>(() => bus.PublishAsync(domEvent));
        sw.Stop();
        // Expect retries: total time >= 7s (2+4+8) but <15s
        Assert.InRange(sw.Elapsed, TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(15));
    }

    private record TestDomainEvent() : FusionOps.Domain.Shared.Interfaces.IDomainEvent
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
    }
}