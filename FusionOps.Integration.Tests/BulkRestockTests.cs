using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Repositories;
using FusionOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;
using System.Diagnostics;

public class BulkRestockTests : IAsyncLifetime
{
    private readonly TestcontainersContainer _pgContainer;
    private FulfillmentContext _ctx = null!;
    private StockRepository _repo = null!;

    public BulkRestockTests()
    {
        _pgContainer = new TestcontainersBuilder<TestcontainersContainer>()
            .WithImage("postgres:16")
            .WithEnvironment("POSTGRES_DB", "fusion")
            .WithEnvironment("POSTGRES_USER", "postgres")
            .WithEnvironment("POSTGRES_PASSWORD", "postgres")
            .WithPortBinding(5432, true)
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _pgContainer.StartAsync();
        var opts = new DbContextOptionsBuilder<FulfillmentContext>()
            .UseNpgsql($"Host=localhost;Port={_pgContainer.GetMappedPublicPort(5432)};Database=fusion;Username=postgres;Password=postgres")
            .Options;
        _ctx = new FulfillmentContext(opts);
        await _ctx.Database.EnsureCreatedAsync();
        _repo = new StockRepository(_ctx);

        // Seed 20k items
        for (int i = 0; i < 20000; i++)
        {
            _ctx.StockItems.Add(new StockItem(new FusionOps.Domain.Shared.Ids.StockItemId(Guid.NewGuid()), $"SKU{i}", 0, 10, new FusionOps.Domain.ValueObjects.Money(1, FusionOps.Domain.Enumerations.Currency.USD)));
        }
        await _ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _pgContainer.DisposeAsync();
    }

    [Fact(Skip = "Disabled due to Docker/Testcontainers issue on this machine")]
    public async Task BulkRestock_ShouldBeFast()
    {
        var deltas = Enumerable.Range(0, 20000).Select(i => new StockItemDelta($"SKU{i}", 5));
        var sw = Stopwatch.StartNew();
        await _repo.BulkRestockAsync(deltas);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 1000, $"Bulk restock took {sw.ElapsedMilliseconds} ms");
    }
}