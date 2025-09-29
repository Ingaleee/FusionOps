using FusionOps.Application.Services.Scenario;
using FusionOps.Application.UseCases.Scenario;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authorization;
using FusionOps.Application.UseCases.Scenario;

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

        // Presets
        endpoints.MapGet("/api/v1/scenario/presets", () =>
        {
            var presets = new[]
            {
                new ScenarioPresetDto("delta10", "+10% demand, no overtime", 10, false, 0),
                new ScenarioPresetDto("delta20", "+20% demand, allow overtime", 20, true, 10),
                new ScenarioPresetDto("budget", "+5% demand, no license overage", 5, true, 0)
            };
            return Results.Ok(presets);
        }).WithMetadata(new AllowAnonymousAttribute());

        // Batch compare
        var compare = endpoints.MapPost("/api/v1/scenario/compare", async (
            CompareScenariosRequest request,
            IScenarioRunner runner,
            CancellationToken ct) =>
        {
            var tasks = request.Commands.Select(c => runner.RunScenario(c, ct));
            var results = await Task.WhenAll(tasks);
            return Results.Ok(results);
        });
        if (env.IsDevelopment()) compare.WithMetadata(new AllowAnonymousAttribute());
        else compare.RequireAuthorization("Scenario.Run");

        return endpoints;
    }
}
