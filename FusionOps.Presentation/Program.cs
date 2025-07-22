using FusionOps.Application;
using FusionOps.Presentation.BackgroundServices;
using FusionOps.Presentation.Extensions;
using FusionOps.Presentation.Modules;
using FusionOps.Infrastructure.Persistence.SqlServer;
using MediatR;

var builder = WebApplication.CreateBuilder(args);

// Infrastructure
builder.Services.AddInfrastructure(builder.Configuration);

// MediatR / AutoMapper
builder.Services.AddMediatR(typeof(AssemblyReference).Assembly);
builder.Services.AddAutoMapper(typeof(AssemblyReference).Assembly);

// Swagger & endpoints
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// HealthChecks
builder.Services.AddHealthChecks();

// Hosted services
builder.Services.AddHostedService<OutboxDispatcher>();

// MediatR pipeline behaviors
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(FusionOps.Application.Pipelines.ValidationBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(FusionOps.Application.Pipelines.LoggingBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(FusionOps.Application.Pipelines.TransactionBehavior<,>));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapWorkforceEndpoints();
app.MapHealthChecks("/health");

app.Run();