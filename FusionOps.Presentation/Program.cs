using System.Text.Json.Serialization;
using FluentValidation;
using FusionOps.Application;
using FusionOps.Domain.Services.Planning;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Persistence.SqlServer;
using FusionOps.Presentation.BackgroundServices;
using FusionOps.Presentation.Extensions;
using FusionOps.Presentation.Middleware;
using FusionOps.Presentation.Modules;
using FusionOps.Presentation.Realtime;
using FusionOps.Infrastructure.Outbox;
using FusionOps.Presentation.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Microsoft.AspNetCore.DataProtection;
using FusionOps.Application.Workflows;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);
var demoMode = builder.Configuration.GetValue<bool>("DemoMode");
if (demoMode && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("DemoMode is available only in Development");
if (demoMode) builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();

builder.Host.UseSerilog((_, log) => log.MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext().Enrich.WithEnvironmentName().WriteTo.Console());
builder.Services.AddSingleton<ResourcePlanner>();
builder.Services.AddSingleton(new WorkflowApprovalPolicy(!builder.Environment.IsDevelopment()));
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
builder.Services.AddAuthentication("Bearer").AddJwtBearer("Bearer", options =>
{
    options.Authority = builder.Configuration["Keycloak:Authority"]
        ?? (builder.Environment.IsDevelopment() ? "http://localhost:8443/realms/fusion" : "https://localhost:8443/realms/fusion");
    options.Audience = "fusion-api";
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            if (context.Principal is not null)
                FusionOps.Presentation.Authorization.KeycloakRoleClaims.Apply(context.Principal, "fusion-api");
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ManageResources", p => p.RequireRole("Resource.Manager"));
    options.AddPolicy("AdminStock", p => p.RequireRole("Stock.Admin"));
    options.AddPolicy("License.Manager", p => p.RequireRole("License.Manager"));
    options.AddPolicy("Scenario.Run", p => p.RequireRole("Resource.Manager"));
    options.AddPolicy("ManageReservations", p => p.RequireRole("Resource.Manager").RequireRole("License.Manager"));
});
builder.Services.AddOpenTelemetry().ConfigureResource(r => r.AddService("fusionops-api"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddSource("FusionOps").AddOtlpExporter())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddMeter(OutboxMetrics.MeterName, ReservationWorkflowRunner.MeterName).AddPrometheusExporter());

if (!demoMode)
{
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddHealthChecks().AddCheck<DatabaseReadinessHealthCheck>("command-stores");
    builder.Services.AddPresentationServices(builder.Configuration);
    builder.Services.AddMediatR(typeof(AssemblyReference).Assembly, typeof(WorkforceContext).Assembly);
    builder.Services.AddValidatorsFromAssembly(typeof(AssemblyReference).Assembly);
    builder.Services.AddSignalR();
    builder.Services.AddHostedService<OutboxDispatcher>();
    if (builder.Configuration.GetValue("Workflows:WorkerEnabled", true))
        builder.Services.AddHostedService<ReservationWorkflowWorker>();
    if (builder.Configuration.GetValue<bool>("Cdc:Enabled"))
    {
        builder.Services.AddHttpClient("debezium");
        builder.Services.AddHostedService<CdcConnectorRegistrationService>();
        builder.Services.AddHostedService<CdcKafkaListener>();
    }
}

var app = builder.Build();
app.UseMiddleware<ExceptionMiddleware>();
app.UseAuthentication();
app.UseMiddleware<CorrelationMiddleware>();
app.UseAuthorization();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.MapPlanningEndpoints();
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapPrometheusScrapingEndpoint();
app.MapGet("/", () => Results.Ok(new { service = "FusionOps", mode = demoMode ? "planning-demo" : "full", planning = "/api/v1/planning/preview" }));

if (!demoMode)
{
    if (app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Database:Initialize"))
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<WorkforceContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<FulfillmentContext>().Database.MigrateAsync();
    }
    app.MapWorkforceEndpoints();
    app.MapCatalogEndpoints();
    app.MapPlanningQuoteEndpoints();
    app.MapWorkflowEndpoints();
    app.MapStockEndpoints();
    app.MapLicenseEndpoints();
    app.MapProjectEndpoints();
    app.MapAuditEndpoints();
    app.MapScenarioEndpoints();
    app.MapHub<NotificationHub>("/hubs/notify");
}
app.Run();

public partial class Program { }
