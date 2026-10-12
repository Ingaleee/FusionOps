using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;
using System.Security.Claims;
using FusionOps.Presentation.Authorization;
using Microsoft.AspNetCore.DataProtection;

namespace FusionOps.Application.Tests;

public sealed class AuthorizationTests
{
    [Fact]
    public void KeycloakMapsRealmAndAudienceRolesWithoutGrantingOtherClientRoles()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("realm_access", "{\"roles\":[\"Resource.Manager\"]}"),
            new Claim("resource_access", "{\"fusion-api\":{\"roles\":[\"Stock.Admin\"]},\"other-client\":{\"roles\":[\"Admin\"]}}")
        }, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        KeycloakRoleClaims.Apply(principal, "fusion-api");
        KeycloakRoleClaims.Apply(principal, "fusion-api");
        Assert.True(principal.IsInRole("Resource.Manager"));
        Assert.True(principal.IsInRole("Stock.Admin"));
        Assert.False(principal.IsInRole("Admin"));
        Assert.Equal(2, principal.FindAll(identity.RoleClaimType).Count());
    }
    private sealed class ProductionFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Production")
            .ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
            });
    }

    [Theory]
    [InlineData("/api/v1/projects/allocations?ids=10000000-0000-0000-0000-000000000001")]
    [InlineData("/api/v1/stock/forecast?days=7")]
    [InlineData("/api/v1/licenses/compiler")]
    [InlineData("/api/v1/audit/allocations/10000000-0000-0000-0000-000000000001")]
    public async Task ProductionReadEndpointsRejectAnonymousCallers(string route)
    {
        await using var factory = new ProductionFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task ProductionReservationRejectsAnonymousCallersBeforeDatabaseAccess()
    {
        await using var factory = new ProductionFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/allocate", new
        {
            projectId = Guid.NewGuid(), resourceIds = new[] { Guid.NewGuid() },
            periodFrom = "2026-10-20T08:00:00Z", periodTo = "2026-10-20T10:00:00Z"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
