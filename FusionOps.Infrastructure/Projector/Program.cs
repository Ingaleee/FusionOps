using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FusionOps.Infrastructure.Projector;
using EventStore.Client;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<EventStoreClient>(sp =>
{
    var settings = EventStoreClientSettings.Create("esdb://eventstore:2113?tls=false");
    return new EventStoreClient(settings);
});

// Простой options-образный доступ к строке подключения
var pg = builder.Configuration.GetConnectionString("Postgres") ?? "Host=localhost;Username=sa;Password=yourStrong(!)Password;Database=FusionOps";

builder.Services.AddHostedService(sp => new AuditProjector(sp.GetRequiredService<EventStoreClient>(), pg));

var app = builder.Build();
app.Run();