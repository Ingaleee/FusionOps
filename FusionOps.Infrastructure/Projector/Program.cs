using EventStore.Client;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Projector;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(new EventStoreClient(EventStoreClientSettings.Create(
    builder.Configuration["EventStore:ConnectionString"] ?? "esdb://localhost:2113?tls=false")));
builder.Services.AddDbContextFactory<FulfillmentContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("pg") ?? "Host=localhost;Port=5433;Database=fusionops;Username=fusionops;Password=fusionops"));
builder.Services.AddHostedService<AuditProjector>();
var app = builder.Build();
app.MapGet("/health/live", () => Results.Ok());
app.Run();
