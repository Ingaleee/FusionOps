using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FusionOps.Application.Tests;

public sealed class PlanningApiTests
{
    private sealed class DemoFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development")
            .ConfigureServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider());
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?> { ["DemoMode"] = "true" }));
            return base.CreateHost(builder);
        }
    }

    [Fact]
    public async Task DemoPlansThroughHttpWithoutInfrastructureAndPropagatesCorrelation()
    {
        await using var factory = new DemoFactory();
        using var client = factory.CreateClient();
        using var mode = JsonDocument.Parse(await client.GetStringAsync("/"));
        Assert.Equal("planning-demo", mode.RootElement.GetProperty("mode").GetString());
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "planning-test-42");
        var response = await client.PostAsJsonAsync("/api/v1/planning/preview", Request());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("planning-test-42", response.Headers.GetValues("X-Correlation-Id").Single());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("isComplete").GetBoolean());
        Assert.Equal(60m, body.RootElement.GetProperty("totalCost").GetProperty("amount").GetDecimal());
        Assert.Equal(body.RootElement.GetProperty("objective").GetDecimal(), body.RootElement.GetProperty("dualObjective").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task InvalidPeriodReturnsProblemDetails()
    {
        await using var factory = new DemoFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/planning/preview", new
        {
            projectId = Guid.NewGuid(), from = "2026-10-20T08:00:00Z", to = "2026-10-20T08:00:00Z",
            currency = "USD", slots = Array.Empty<object>(), candidates = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    private static object Request() => new
    {
        projectId = Guid.NewGuid(), from = "2026-10-20T08:00:00Z", to = "2026-10-20T10:00:00Z", currency = "USD",
        slots = new[]
        {
            new { id = Guid.NewGuid(), name = "General", kind = "Human", requiredSkills = new Dictionary<string, int>() },
            new { id = Guid.NewGuid(), name = "Specialist", kind = "Human", requiredSkills = new Dictionary<string, int> { ["csharp"] = 3 } }
        },
        candidates = new[]
        {
            new { id = Guid.NewGuid(), name = "Specialist", kind = "Human", hourlyRate = 10m, skills = new Dictionary<string, int> { ["csharp"] = 4 } },
            new { id = Guid.NewGuid(), name = "General", kind = "Human", hourlyRate = 20m, skills = new Dictionary<string, int>() }
        }
    };
}
