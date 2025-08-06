using FusionOps.Application.Pipelines;
using EventStore.Client;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<EventStoreClient>(sp =>
        {
            var settings = EventStoreClientSettings.Create("esdb://localhost:2113?tls=false");
            return new EventStoreClient(settings);
        });
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));
        return services;
    }
}