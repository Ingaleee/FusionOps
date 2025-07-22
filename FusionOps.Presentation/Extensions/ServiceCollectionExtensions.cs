using FusionOps.Domain.Interfaces;
using FusionOps.Infrastructure.Messaging;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Persistence.SqlServer;
using FusionOps.Infrastructure.Repositories;
using FusionOps.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FusionOps.Presentation.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddDbContext<WorkforceContext>(o => o.UseSqlServer(cfg.GetConnectionString("sql")));
        services.AddDbContext<FulfillmentContext>(o => o.UseNpgsql(cfg.GetConnectionString("pg")));

        services.AddScoped<IAllocationRepository, AllocationRepository>();
        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddSingleton<IEventBus, ConsoleEventBus>();

        return services;
    }
} 