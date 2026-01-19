using FusionOps.Application;
using FusionOps.Presentation.BackgroundServices;
using FusionOps.Presentation.Extensions;
using FusionOps.Presentation.Modules;
using FusionOps.Infrastructure.Persistence.SqlServer;
using MediatR;
using Serilog;
using Serilog.Events;
using FusionOps.Presentation.Middleware;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using FusionOps.Presentation.Realtime;
using Microsoft.EntityFrameworkCore;

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

// Add middleware
builder.Services.AddTransient<CorrelationMiddleware>();
builder.Services.AddTransient<ExceptionMiddleware>();
builder.Services.AddTransient<UserContextEnricher>();

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

// MediatR pipeline behaviors
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(FusionOps.Application.Pipelines.ValidationBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(FusionOps.Application.Pipelines.LoggingBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(FusionOps.Application.Pipelines.TransactionBehavior<,>));

// SignalR
builder.Services.AddSignalR();
// (╨╛╨┐╤Ж╨╕╨╛╨╜╨░╨╗╤М╨╜╨╛) Serilog Sink
// builder.Host.UseSerilog((ctx, cfg) =>
//     cfg.WriteTo.SignalR(builder.Services, "/hubs/notify", restrictedToMinimumLevel: LogEventLevel.Information));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapWorkforceEndpoints();
app.MapStockEndpoints();
app.MapHealthChecks("/health");
app.MapHub<NotificationHub>("/hubs/notify");

app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<UserContextEnricher>();
app.UseMiddleware<ExceptionMiddleware>();

// Ensure database constraints are applied on startup
using (var scope = app.Services.CreateScope())
{
    var workforce = scope.ServiceProvider.GetRequiredService<WorkforceContext>();
    try
    {
        // Apply overlap prevention trigger if not exists
        var sql = @"
            IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name = 'fn_CheckAllocationOverlap' AND type = 'FN')
            BEGIN
                EXEC('CREATE FUNCTION fn_CheckAllocationOverlap(@ResourceId UNIQUEIDENTIFIER, @PeriodStart DATETIMEOFFSET, @PeriodEnd DATETIMEOFFSET, @ExcludeAllocationId UNIQUEIDENTIFIER = NULL)
                RETURNS BIT AS BEGIN
                    DECLARE @HasOverlap BIT = 0;
                    IF EXISTS (SELECT 1 FROM Allocations WHERE ResourceId = @ResourceId AND Id != ISNULL(@ExcludeAllocationId, ''00000000-0000-0000-0000-000000000000'') AND @PeriodStart < PeriodEnd AND PeriodStart < @PeriodEnd)
                        SET @HasOverlap = 1;
                    RETURN @HasOverlap;
                END');
            END
            
            IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name = 'trg_PreventAllocationOverlap' AND type = 'TR')
            BEGIN
                EXEC('CREATE TRIGGER trg_PreventAllocationOverlap ON Allocations AFTER INSERT, UPDATE AS BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM inserted i CROSS APPLY (SELECT fn_CheckAllocationOverlap(i.ResourceId, i.PeriodStart, i.PeriodEnd, i.Id) AS HasOverlap) check_overlap WHERE check_overlap.HasOverlap = 1)
                    BEGIN
                        RAISERROR(''Allocation overlap detected'', 16, 1);
                        ROLLBACK TRANSACTION;
                        RETURN;
                    END
                END');
            END";
        workforce.Database.ExecuteSqlRaw(sql);
    }
    catch
    {
        // Ignore if already exists or database not ready
    }
}

app.Run();
