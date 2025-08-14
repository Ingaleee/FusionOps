using FusionOps.Application;
using FusionOps.Application.Abstractions;
using FusionOps.Presentation.BackgroundServices;
using FusionOps.Presentation.Extensions;
using FusionOps.Presentation.Modules;
using FusionOps.Infrastructure.Persistence.SqlServer;
using FusionOps.Infrastructure.Persistence.Postgres;
using MediatR;
using Serilog;
using Serilog.Events;
using FusionOps.Presentation.Middleware;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using FusionOps.Presentation.Realtime;

var builder = WebApplication.CreateBuilder(args);

// Infrastructure
builder.Services.AddInfrastructure(builder.Configuration);

// MediatR / AutoMapper
builder.Services.AddMediatR(typeof(AssemblyReference).Assembly);
builder.Services.AddAutoMapper(typeof(AssemblyReference).Assembly);

// top after builder creation
builder.Host.UseSerilog((ctx, cfg) =>
{
    cfg.MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
       .Enrich.FromLogContext()
       .Enrich.WithEnvironmentName()
       .WriteTo.Console();
});

builder.Services.AddOpenTelemetry()
    .WithTracing(tracer =>
    {
        tracer.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("fusionops-api"))
              .AddAspNetCoreInstrumentation()
              .AddEntityFrameworkCoreInstrumentation()
              .AddJaegerExporter();
    })
    .WithMetrics(m =>
    {
        m.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("fusionops-api"))
         .AddAspNetCoreInstrumentation()
         .AddPrometheusExporter();
    });

// AuthN/Z
builder.Services.AddAuthentication("Bearer")
       .AddJwtBearer("Bearer", o =>
       {
           o.Authority = builder.Configuration["Keycloak:Authority"] ?? "https://localhost:8443/realms/fusion";
           o.Audience = "fusion-api";
           o.RequireHttpsMetadata = false;
       });

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("ManageResources", p => p.RequireRole("Resource.Manager"));
    opts.AddPolicy("AdminStock", p => p.RequireRole("Stock.Admin"));
});

// Middlewares are added to the pipeline directly via app.UseMiddleware<>()

// Swagger & endpoints
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme."
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            }, new string[] {}
        }
    });
});

// HealthChecks
builder.Services.AddHealthChecks();

// Hosted services
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddHostedService<CdcConnectorRegistrationService>();
builder.Services.AddHostedService<CdcKafkaListener>();

builder.Services.AddHttpClient("debezium");

// SignalR
builder.Services.AddSignalR();
builder.Services.AddScoped<IResourceNotification, SignalRNotificationService>();

// Presentation services (EventStore, pipeline, policies)
builder.Services.AddPresentationServices(builder.Configuration);

var app = builder.Build();

// Ensure SQL database exists (dev convenience)
try
{
    using var scope = app.Services.CreateScope();
    var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var connStr = cfg.GetConnectionString("sql");
    if (!string.IsNullOrWhiteSpace(connStr))
    {
        var csb = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connStr);
        var dbName = csb.InitialCatalog;
        csb.InitialCatalog = "master";
        using var master = new Microsoft.Data.SqlClient.SqlConnection(csb.ConnectionString);
        await master.OpenAsync();
        using var cmd = master.CreateCommand();
        cmd.CommandText = $"IF DB_ID('{dbName}') IS NULL CREATE DATABASE [{dbName}]";
        await cmd.ExecuteNonQueryAsync();
    }

    // Create schema if missing (no migrations in repo)
    var wf = scope.ServiceProvider.GetRequiredService<FusionOps.Infrastructure.Persistence.SqlServer.WorkforceContext>();
    await wf.Database.EnsureCreatedAsync();
    var saga = scope.ServiceProvider.GetRequiredService<FusionOps.Infrastructure.Persistence.SqlServer.AllocationSagaContext>();
    await saga.Database.EnsureCreatedAsync();

    // Ensure Postgres schemas exist for fulfillment (dev convenience)
    var ff = scope.ServiceProvider.GetRequiredService<FusionOps.Infrastructure.Persistence.Postgres.FulfillmentContext>();
    await ff.Database.EnsureCreatedAsync();
}
catch { }

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapWorkforceEndpoints();
app.MapStockEndpoints();
app.MapAuditEndpoints();
app.MapProjectEndpoints();
app.MapHealthChecks("/health");
app.MapHub<NotificationHub>("/hubs/notify");

app.MapPrometheusScrapingEndpoint();

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<UserContextEnricher>();
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<AuditResponseHeadersMiddleware>();

app.Run();