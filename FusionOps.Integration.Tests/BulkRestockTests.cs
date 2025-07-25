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
    private readonly PostgreSqlTestcontainer _pgContainer;
    private FulfillmentContext _ctx = null!;
    private StockRepository _repo = null!;

    public BulkRestockTests()
    {
        _pgContainer = new TestcontainersBuilder<PostgreSqlTestcontainer>()
            .WithDatabase(new PostgreSqlTestcontainerConfiguration()
            {
                Database = "fusion",
                Username = "postgres",
                Password = "postgres"
            })
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _pgContainer.StartAsync();
        var opts = new DbContextOptionsBuilder<FulfillmentContext>()
            .UseNpgsql(_pgContainer.ConnectionString)
            .Options;
        _ctx = new FulfillmentContext(opts);
        await _ctx.Database.EnsureCreatedAsync();
        _repo = new StockRepository(_ctx);

        // Seed 20k items
        for (int i = 0; i < 20000; i++)
        {
            _ctx.StockItems.Add(new StockItem(new Domain.Shared.Ids.StockItemId(Guid.NewGuid()), $"SKU{i}", 0, 10, new Domain.ValueObjects.Money(1, "USD")));
        }
        await _ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _pgContainer.DisposeAsync();
    }

    [Fact]
    public async Task BulkRestock_ShouldBeFast()
    {
        var deltas = Enumerable.Range(0, 20000).Select(i => new StockItemDelta($"SKU{i}", 5));
        var sw = Stopwatch.StartNew();
        await _repo.BulkRestockAsync(deltas);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 1000, $"Bulk restock took {sw.ElapsedMilliseconds} ms");
    }
} 