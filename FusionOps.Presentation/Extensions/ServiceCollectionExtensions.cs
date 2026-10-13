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
using FusionOps.Application.Workflows;
using FusionOps.Infrastructure.Workflows;
using FusionOps.Application.Planning;
using FusionOps.Infrastructure.Planning;
using FusionOps.Infrastructure.Tenancy;

namespace FusionOps.Presentation.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration cfg)
    {
        var sql = cfg.GetConnectionString("sql") ?? throw new InvalidOperationException("SQL connection is required");
        var pg = cfg.GetConnectionString("pg") ?? throw new InvalidOperationException("PostgreSQL connection is required");
        services.AddSingleton<IDbContextFactory<WorkforceContext>>(new TenantWorkforceFactory(sql, cfg.GetConnectionString("sqlWorker") ?? sql));
        services.AddSingleton<IDbContextFactory<FulfillmentContext>>(new TenantFulfillmentFactory(pg, cfg.GetConnectionString("pgWorker") ?? pg));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<WorkforceContext>>().CreateDbContext());
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<FulfillmentContext>>().CreateDbContext());

        services.AddScoped<IAllocationRepository, AllocationRepository>();
        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<ILicenseRepository, LicenseRepository>();
        services.AddScoped<IHumanResourceRepository, HumanResourceRepository>();
        services.AddScoped<IEquipmentResourceRepository, EquipmentResourceRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IReservationWorkflowStore, PostgresReservationWorkflowStore>();
        services.AddScoped<IWorkforceWorkflowParticipant, SqlWorkforceWorkflowParticipant>();
        services.AddScoped<ILicenseWorkflowParticipant, PostgresLicenseWorkflowParticipant>();
        services.AddScoped<ReservationWorkflowService>();
        services.AddScoped<ReservationWorkflowRunner>();
        services.AddScoped<IResourceCatalogStore, SqlResourceCatalogStore>();
        services.AddScoped<IPlanningQuoteStore, SqlPlanningQuoteStore>();
        services.AddScoped<FusionOps.Application.Projects.IProjectStore, FusionOps.Infrastructure.Projects.PostgresProjectStore>();
        services.AddScoped<FusionOps.Application.Projects.IProjectAccess>(sp => sp.GetRequiredService<FusionOps.Application.Projects.IProjectStore>());
        services.AddScoped<IOptimizerStrategy, FusionOps.Domain.Services.HungarianOptimizerStrategy>();
        services.AddSingleton<IStockForecaster, FusionOps.Infrastructure.Optimizers.MlNetOptimizer>();

        services.AddMassTransit(mtCfg =>
        {
            mtCfg.SetKebabCaseEndpointNameFormatter();


            mtCfg.UsingRabbitMq((ctx, busCfg) =>
            {
                busCfg.Host(cfg["Rabbit:Host"] ?? "localhost", "/", h =>
                {
                    h.Username(cfg["Rabbit:Username"] ?? "guest");
                    h.Password(cfg["Rabbit:Password"] ?? "guest");
                });
                busCfg.ConfigureEndpoints(ctx);
            });
        });

        services.AddScoped<IEventBus, RabbitBus>();
        if (cfg.GetValue<bool>("Telemetry:KafkaEnabled"))
            services.AddSingleton<ITelemetryProducer, KafkaTelemetryProducer>();
        else
            services.AddSingleton<ITelemetryProducer, DisabledTelemetryProducer>();

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
        // Pipeline: validation -> logging -> one database transaction with a durable outbox.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(FusionOps.Application.Pipelines.ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(FusionOps.Application.Pipelines.LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(FusionOps.Application.Pipelines.TransactionBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(FusionOps.Application.Pipelines.IdempotencyBehavior<,>));
        services.AddScoped<ICommandReceiptStore, FusionOps.Infrastructure.Idempotency.SqlCommandReceiptStore>();

        services.AddSingleton(sp => new EventStore.Client.EventStoreClient(
            EventStore.Client.EventStoreClientSettings.Create(cfg["EventStore:ConnectionString"] ?? "esdb://localhost:2113?tls=false")));
        services.AddSingleton<IAuditWriter, EventStoreAuditWriter>();
        services.AddScoped<IExecutionContext, HttpExecutionContext>();
        services.AddScoped<FusionOps.Infrastructure.Outbox.OutboxProcessor>();
        services.AddScoped<IResourceNotification, FusionOps.Presentation.Realtime.SignalRNotificationService>();
        services.AddSingleton(TimeProvider.System);

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
