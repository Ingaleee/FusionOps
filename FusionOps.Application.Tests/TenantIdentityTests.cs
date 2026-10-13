using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using FusionOps.Application.Abstractions;
using FusionOps.Application.Planning;
using FusionOps.Domain.Catalog;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.ValueObjects;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace FusionOps.Application.Tests;

public sealed class TenantIdentityTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("FusionOps-only-a-local-JWT-test-key-2026"));
    private sealed class JwtFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Production").ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.PostConfigure<JwtBearerOptions>("Bearer", options =>
            {
                options.Authority = null; options.MetadataAddress = string.Empty;
                options.ConfigurationManager = new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(
                    new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration { Issuer = "fusionops-tests" });
                options.TokenValidationParameters = new() { ValidIssuer = "fusionops-tests", ValidAudience = "fusion-api", IssuerSigningKey = Key,
                    ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true };
            });
            var catalog = new Mock<IResourceCatalogStore>();
            catalog.Setup(s => s.ListAsync(It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(() =>
            {
                var resource = CatalogResource.Register(Guid.NewGuid(), TenantExecution.RequiredTenant.ToString("D"), ResourceKind.Human, Money.Usd(25), new Dictionary<string, int>());
                resource.BindTenant(TenantExecution.RequiredTenant);
                return Task.FromResult<IReadOnlyList<CatalogResource>>([resource]);
            });
            services.RemoveAll<IResourceCatalogStore>(); services.AddSingleton(catalog.Object);
        });
    }
    private static string Token(params Claim[] tenantClaims) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("fusionops-tests", "fusion-api",
        [new(JwtRegisteredClaimNames.Sub, "same-subject"), new(ClaimTypes.Role, "Resource.Manager"), .. tenantClaims],
        DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5), new SigningCredentials(Key, SecurityAlgorithms.HmacSha256)));

    [Fact]
    public async Task VerifiedJwtSelectsTheTenantAndAHeaderCannotOverrideIt()
    {
        await using var factory = new JwtFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(new Claim("tenant_id", A.ToString("D"))));
        var response = await client.GetAsync("/api/v1/catalog/resources");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(A.ToString("D"), await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add("X-Tenant-Id", B.ToString("D"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/catalog/resources")).StatusCode);
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    public async Task VerifiedTokenWithMissingAmbiguousOrInvalidTenantFailsClosed(string kind)
    {
        var claims = kind switch
        {
            "missing" => Array.Empty<Claim>(), "invalid" => [new Claim("tenant_id", "not-a-guid")],
            "empty" => [new Claim("tenant_id", Guid.Empty.ToString("D"))],
            _ => new Claim[] { new("tenant_id", A.ToString("D")), new("tenant_id", B.ToString("D")) }
        };
        await using var factory = new JwtFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(claims));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/catalog/resources")).StatusCode);
    }
    [Fact]
    public async Task SimultaneousRequestsKeepTheirVerifiedTenantInSeparateExecutionContexts()
    {
        await using var factory = new JwtFactory(); using var client = factory.CreateClient();
        async Task<string> Read(Guid tenant)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog/resources");
            request.Headers.Authorization = new("Bearer", Token(new Claim("tenant_id", tenant.ToString("D"))));
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }
        var results = await Task.WhenAll(Read(A), Read(B));
        Assert.Contains(A.ToString("D"), results[0]); Assert.DoesNotContain(B.ToString("D"), results[0]);
        Assert.Contains(B.ToString("D"), results[1]); Assert.DoesNotContain(A.ToString("D"), results[1]);
        Assert.Null(TenantExecution.Current);
    }
}
