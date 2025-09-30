using System;
using System.Linq;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FusionOps.Application.Abstractions;
using FusionOps.Domain.Entities;
using FusionOps.Infrastructure.Persistence.Common;
using FusionOps.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FusionOps.Integration.Tests;

public sealed class RlsIsolationTests : IAsyncLifetime
{
    private IContainer? _pg;
    private string? _conn;
    private bool _skip;

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new ContainerBuilder()
                .WithImage("postgres:16-alpine")
                .WithEnvironment("POSTGRES_PASSWORD", "postgres")
                .WithEnvironment("POSTGRES_DB", "fusionops")
                .WithPortBinding(5544, 5432)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
                .Build();
            await _pg.StartAsync();
            _conn = "Host=localhost;Port=5544;Database=fusionops;Username=postgres;Password=postgres";
        }
        catch
        {
            _skip = true;
        }
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null)
        {
            await _pg.StopAsync();
            await _pg.DisposeAsync();
        }
    }

    private static ServiceProvider BuildServices(string connectionString, string tenantId)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantProvider>(new TestTenantProvider(tenantId));
        services.AddSingleton<NpgsqlTenantConnectionInterceptor>();
        services.AddDbContext<FulfillmentContext>((sp, o) =>
        {
            var interceptor = sp.GetRequiredService<NpgsqlTenantConnectionInterceptor>();
            o.UseNpgsql(connectionString);
            o.AddInterceptors(interceptor);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Rows_are_isolated_between_tenants_via_RLS()
    {
        if (_skip || _conn is null) return; // skip if docker is unavailable

        // Prepare DB schema and RLS
        var spInit = BuildServices(_conn!, "acme");
        await using (spInit as IAsyncDisposable)!
        await using (var scope = spInit.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<FulfillmentContext>();
            await ctx.Database.EnsureCreatedAsync();
            await FusionOps.Infrastructure.Persistence.Postgres.Configurations.RlsInitializer.EnsureRlsAsync(ctx);
        }

        // Insert under tenant acme
        var spA = BuildServices(_conn!, "acme");
        await using (spA as IAsyncDisposable)!
        await using (var scopeA = spA.CreateAsyncScope())
        {
            var ctxA = scopeA.ServiceProvider.GetRequiredService<FulfillmentContext>();
            ctxA.StockItems.Add(new StockItem
            {
                Id = Guid.NewGuid(),
                Sku = "CPU-I9",
                Quantity = 10,
                WarehouseId = Guid.NewGuid()
            });
            await ctxA.SaveChangesAsync();
            var countA = await ctxA.StockItems.Where(s => s.Sku == "CPU-I9").CountAsync();
            Assert.Equal(1, countA);
        }

        // Read under tenant zen: should see 0
        var spB = BuildServices(_conn!, "zen");
        await using (spB as IAsyncDisposable)!
        await using (var scopeB = spB.CreateAsyncScope())
        {
            var ctxB = scopeB.ServiceProvider.GetRequiredService<FulfillmentContext>();
            var countB = await ctxB.StockItems.Where(s => s.Sku == "CPU-I9").CountAsync();
            Assert.Equal(0, countB);
        }
    }

    private sealed class TestTenantProvider : ITenantProvider
    {
        public TestTenantProvider(string tenantId) { TenantId = tenantId; }
        public string TenantId { get; }
        public bool IsSet => true;
    }
}



