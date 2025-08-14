using FusionOps.Application.Services.Scenario;
using FusionOps.Application.UseCases.Scenario;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authorization;

namespace FusionOps.Presentation.Modules;

public static class ScenarioEndpoints
{
    public static IEndpointRouteBuilder MapScenarioEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var env = endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>();

        var runScenario = endpoints.MapPost("/api/v1/scenario/run", async (
            RunScenarioCommand command,
            IScenarioRunner scenarioRunner,
            CancellationToken cancellationToken) =>
        {
            var result = await scenarioRunner.RunScenario(command, cancellationToken);
            return Results.Ok(result);
        });

        if (env.IsDevelopment())
        {
            runScenario.WithMetadata(new AllowAnonymousAttribute());
        }
        else
        {
            runScenario.RequireAuthorization("Scenario.Run");
        }

        return endpoints;
    }
}
