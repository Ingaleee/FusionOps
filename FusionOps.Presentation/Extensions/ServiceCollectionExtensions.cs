using FusionOps.Domain.Interfaces;
using FusionOps.Infrastructure.Messaging;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Persistence.SqlServer;
using FusionOps.Infrastructure.Repositories;
using FusionOps.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MassTransit;
using FusionOps.Presentation.Authorization;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using FusionOps.Application.Abstractions;
using FusionOps.Presentation.Infrastructure;
using FusionOps.Infrastructure.Costing;
using Microsoft.Extensions.Options;
using FusionOps.Infrastructure.Persistence.Postgres.Configurations;

namespace FusionOps.Presentation.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddDbContext<WorkforceContext>(o => o.UseSqlServer(cfg.GetConnectionString("sql")));
        services.AddDbContext<FulfillmentContext>(o => o.UseNpgsql(cfg.GetConnectionString("pg")));

        services.AddScoped<IAllocationRepository, AllocationRepository>();
        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<ILicenseRepository, LicenseRepository>();
        services.AddScoped<IHumanResourceRepository, HumanResourceRepository>();
        services.AddScoped<IEquipmentResourceRepository, EquipmentResourceRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IOptimizerStrategy, FusionOps.Domain.Services.HungarianOptimizerStrategy>();
        services.AddSingleton<IStockForecaster, FusionOps.Infrastructure.Optimizers.MlNetOptimizer>();
        services.AddDbContext<FusionOps.Infrastructure.Persistence.SqlServer.AllocationSagaContext>(o =>
            o.UseSqlServer(cfg.GetConnectionString("sql")));

        services.AddMassTransit(mtCfg =>
        {
            mtCfg.SetKebabCaseEndpointNameFormatter();

            mtCfg.AddSagaStateMachine<FusionOps.Infrastructure.Saga.AllocationStateMachine, FusionOps.Infrastructure.Saga.AllocationState>()
                .EntityFrameworkRepository(r =>
                {
                    r.AddDbContext<FusionOps.Infrastructure.Persistence.SqlServer.AllocationSagaContext, FusionOps.Infrastructure.Persistence.SqlServer.AllocationSagaContext>((provider, builder) =>
                    {
                        builder.UseSqlServer(cfg.GetConnectionString("sql"), sql => sql.MigrationsAssembly(typeof(FusionOps.Infrastructure.Persistence.SqlServer.AllocationSagaContext).Assembly.FullName));
                    });
                });

            mtCfg.UsingRabbitMq((ctx, busCfg) =>
            {
                busCfg.Host(cfg.GetSection("Rabbit")?["Host"] ?? "localhost", "/", h => { });
                busCfg.ConfigureEndpoints(ctx);
            });
        });

        services.AddSingleton<IEventBus, RabbitBus>();
        services.AddSingleton<ITelemetryProducer, KafkaTelemetryProducer>();

        // Costing
        services.AddOptions<CostingOptions>()
            .Bind(cfg.GetSection("Costing"))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<ICostEngine, DefaultCostEngine>();
        services.AddScoped<FusionOps.Application.Services.Scenario.IScenarioRunner, FusionOps.Application.Services.Scenario.ScenarioRunner>();

        return services;
    }

    public static IServiceCollection AddPresentationServices(this IServiceCollection services, IConfiguration cfg)
    {
        // Pipeline: validation -> logging -> transaction -> audit
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(FusionOps.Application.Pipelines.ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(FusionOps.Application.Pipelines.LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(FusionOps.Application.Pipelines.TransactionBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(FusionOps.Application.Pipelines.AuditBehavior<,>));

        // Audit writer (no-op writer to keep build simple in this environment)
        services.AddSingleton<IAuditWriter, EventStoreAuditWriter>();

        services.AddScoped<IAuthorizationHandler, AuditAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuditReadRequirement.PolicyName, policy =>
                policy.Requirements.Add(new AuditReadRequirement()));
        });

        services.AddHttpContextAccessor();

        return services;
    }
}