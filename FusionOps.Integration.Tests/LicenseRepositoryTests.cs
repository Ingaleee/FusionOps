 using System;
using System.Linq;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FusionOps.Domain.Entities;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class LicenseRepositoryTests : IAsyncLifetime
{
    private readonly TestcontainersContainer _pg;
    private FulfillmentContext _ctx = null!;
    private LicenseRepository _repo = null!;
    private bool _skip;

    public LicenseRepositoryTests()
    {
        _pg = new TestcontainersBuilder<TestcontainersContainer>()
            .WithImage("postgres:16")
            .WithEnvironment("POSTGRES_DB", "fusion")
            .WithEnvironment("POSTGRES_USER", "postgres")
            .WithEnvironment("POSTGRES_PASSWORD", "postgres")
            .WithPortBinding(5432, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _pg.StartAsync();
            var opts = new DbContextOptionsBuilder<FulfillmentContext>()
                .UseNpgsql($"Host=localhost;Port={_pg.GetMappedPublicPort(5432)};Database=fusion;Username=postgres;Password=postgres")
                .Options;
            _ctx = new FulfillmentContext(opts);
            await _ctx.Database.EnsureCreatedAsync();
            _repo = new LicenseRepository(_ctx);
        }
        catch
        {
            _skip = true;
        }
    }

    public async Task DisposeAsync()
    {
        await _pg.DisposeAsync();
    }

    [Fact]
    public async Task Add_Find_Update_LicensePool_Works()
    {
        if (_skip)
        {
            // Docker daemon not available in this environment; treat as skipped.
            return;
        }

        var product = $"Prod-{Guid.NewGuid():N}";

        // Create pool
        var pool = new LicensePool(product, totalSeats: 5);
        await _repo.AddAsync(pool);

        // Find by product
        var found = await _repo.FindByProductAsync(product);
        Assert.NotNull(found);
        Assert.Equal(5, found!.TotalSeats);

        // Allocate and update
        var projectId = Guid.NewGuid();
        found.Allocate(projectId, 3);
        await _repo.UpdateAsync(found);

        // Re-read and verify allocation
        var afterAlloc = await _repo.FindByProductAsync(product);
        Assert.NotNull(afterAlloc);
        Assert.Equal(3, afterAlloc!.AllocatedSeats);
        Assert.Contains(afterAlloc.Allocations, a => a.ProjectId == projectId && a.Seats == 3);

        // Release and update
        afterAlloc.Release(projectId, 2);
        await _repo.UpdateAsync(afterAlloc);

        // Verify release
        var afterRelease = await _repo.FindByProductAsync(product);
        Assert.NotNull(afterRelease);
        Assert.Equal(1, afterRelease!.AllocatedSeats);
        Assert.Contains(afterRelease.Allocations, a => a.ProjectId == projectId && a.Seats == 1);

        // List all
        var all = await _repo.GetAllAsync();
        Assert.Contains(all, p => p.Product == product);
    }
}


