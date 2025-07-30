using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FusionOps.Domain.Entities;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Repositories;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class LowStockSignalRTests : IAsyncLifetime
{
    private readonly TestcontainersContainer _pgContainer;
    private FulfillmentContext _ctx = null!;
    private StockRepository _repo = null!;
    private HubConnection _hub = null!;
    private int _lowStockQty = -1;
    private readonly string _sku = "SKU999";

    public LowStockSignalRTests()
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

        // SignalR клиент
        _hub = new HubConnectionBuilder()
            .WithUrl("http://localhost:5000/hubs/notify")
            .WithAutomaticReconnect()
            .Build();
        _hub.On<object>("lowStock", msg => {
            var qty = (int)msg.GetType().GetProperty("qtyLeft")?.GetValue(msg)!;
            _lowStockQty = qty;
        });
        await _hub.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _hub.DisposeAsync();
        await _pgContainer.DisposeAsync();
    }

    [Fact(Skip = "Demo: requires running backend and SignalR")]
    public async Task Deduct_TriggersLowStockSignalR()
    {
        // Arrange: создаём StockItem с Quantity = 6, ReorderPoint = 5
        var item = new StockItem(new FusionOps.Domain.Shared.Ids.StockItemId(Guid.NewGuid()), _sku, 6, 5, new FusionOps.Domain.ValueObjects.Money(1, FusionOps.Domain.Enumerations.Currency.USD));
        _ctx.StockItems.Add(item);
        await _ctx.SaveChangesAsync();

        // Act: списываем 1, чтобы вызвать ReorderPointReached
        item.Deduct(1);
        await _ctx.SaveChangesAsync();

        // Wait for SignalR event (до 5 сек)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (_lowStockQty == -1 && sw.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(100);

        // Assert
        Assert.Equal(5, _lowStockQty);
    }
} 