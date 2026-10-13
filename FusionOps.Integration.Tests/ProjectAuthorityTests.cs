extern alias api;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FusionOps.Application.Abstractions;
using FusionOps.Application.Projects;
using FusionOps.Domain.Projects;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.ValueObjects;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Persistence.SqlServer;
using FusionOps.Infrastructure.Projects;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace FusionOps.Integration.Tests;

public sealed partial class ReliabilityTests
{
    private static readonly DateTimeOffset ProjectStart = DateTimeOffset.Parse("2026-10-20T08:00:00Z");
    private static readonly JsonSerializerOptions ProjectJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly SymmetricSecurityKey ProjectKey = new(Encoding.UTF8.GetBytes("FusionOps-projects-local-verification-secret-2026"));
    private static ProjectDefinition ProjectSpec(string name = "Installation")
    {
        var site = Guid.NewGuid(); var design = Guid.NewGuid(); var install = Guid.NewGuid();
        return new(name, new(ProjectStart, ProjectStart.AddDays(5)), 5000, "USD",
            [new(site, "Customer site", [new(ProjectStart, ProjectStart.AddDays(5))])],
            [new(design, "Design", site, 60, ProjectStart, ProjectStart.AddDays(5), [], [], [], []),
             new(install, "Install", site, 120, ProjectStart, ProjectStart.AddDays(5), [design], [], [new("connector", 2)], [])],
            [new(Guid.NewGuid(), "Handover", ProjectStart.AddDays(5), [install])]);
    }
    private PostgresProjectStore ProjectStore(string actor = "owner", bool unmanaged = false) =>
        new(new TestContextFactory(_fulfillmentOptions), new TestExecutionContext(actor), TimeProvider.System, new(unmanaged));
    private static async Task<JsonElement> ProjectBody(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync(); Assert.That(response.StatusCode, Is.EqualTo(expected), body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }
    private sealed class ProjectApiFactory(DbContextOptions<WorkforceContext> sqlOptions, DbContextOptions<FulfillmentContext> pgOptions)
        : WebApplicationFactory<api::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Production").ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>(); services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<WorkforceContext>(); services.RemoveAll<FulfillmentContext>();
            services.RemoveAll<IDbContextFactory<WorkforceContext>>(); services.RemoveAll<IDbContextFactory<FulfillmentContext>>();
            services.AddSingleton<IDbContextFactory<WorkforceContext>>(new WorkforceContextFactory(sqlOptions));
            services.AddSingleton<IDbContextFactory<FulfillmentContext>>(new TestContextFactory(pgOptions));
            services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<WorkforceContext>>().CreateDbContext());
            services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<FulfillmentContext>>().CreateDbContext());
            services.PostConfigure<JwtBearerOptions>("Bearer", options =>
            {
                options.Authority = null; options.MetadataAddress = string.Empty;
                options.ConfigurationManager = new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(
                    new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration { Issuer = "fusionops-project-tests" });
                options.TokenValidationParameters = new() { ValidIssuer = "fusionops-project-tests", ValidAudience = "fusion-api", IssuerSigningKey = ProjectKey,
                    ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true };
            });
        });
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?> { ["DemoMode"] = "false", ["Database:Initialize"] = "false" }));
            return base.CreateHost(builder);
        }
        public HttpClient Actor(string subject, Guid tenant, bool managers = true)
        {
            var client = CreateClient(); var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, subject), new("tenant_id", tenant.ToString("D")) };
            if (managers) { claims.Add(new(ClaimTypes.Role, "Resource.Manager")); claims.Add(new(ClaimTypes.Role, "License.Manager")); claims.Add(new(ClaimTypes.Role, "AuditViewer")); }
            var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("fusionops-project-tests", "fusion-api", claims,
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5), new SigningCredentials(ProjectKey, SecurityAlgorithms.HmacSha256)));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); return client;
        }
    }
    private static async Task<HttpResponseMessage> ProjectMutation(HttpClient client, HttpMethod method, string path, object? body, long? version)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: ProjectJson);
        if (version is { } value) request.Headers.TryAddWithoutValidation("If-Match", $"\"{value}\"");
        return await client.SendAsync(request);
    }
    [Test]
    public async Task ProjectHttpPreservesOriginalSubmissionHistoryAndStrongPreconditions()
    {
        await using var factory = new ProjectApiFactory(_workforceOptions, _fulfillmentOptions);
        using var owner = factory.Actor("owner", TenantA); var id = Guid.NewGuid(); var spec = ProjectSpec();
        var create = await owner.PostAsJsonAsync("/api/v1/projects", new { id, definition = spec }, ProjectJson);
        var created = await ProjectBody(create, HttpStatusCode.Created); Assert.That(create.Headers.ETag?.Tag, Is.EqualTo("\"1\""));
        Assert.That(create.Headers.Location?.OriginalString, Is.EqualTo($"/api/v1/projects/{id:D}"));
        await ProjectBody(await owner.PostAsJsonAsync("/api/v1/projects", new { id, definition = spec with { Tasks = spec.Tasks.Reverse().ToArray() } }, ProjectJson), HttpStatusCode.Created);
        var changed = spec with { BudgetAmount = 6000 };
        Assert.That((await ProjectMutation(owner, HttpMethod.Put, $"/api/v1/projects/{id}/definition", changed, null)).StatusCode, Is.EqualTo((HttpStatusCode)428));
        var revision = await ProjectBody(await ProjectMutation(owner, HttpMethod.Put, $"/api/v1/projects/{id}/definition", changed, 1), HttpStatusCode.OK);
        Assert.That(revision.GetProperty("definitionVersion").GetInt64(), Is.EqualTo(2));
        Assert.That(revision.GetProperty("definitionDigest").GetString(), Is.Not.EqualTo(created.GetProperty("definitionDigest").GetString()));
        Assert.That((await ProjectMutation(owner, HttpMethod.Put, $"/api/v1/projects/{id}/definition", spec, 1)).StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
        var replay = await ProjectBody(await owner.PostAsJsonAsync("/api/v1/projects", new { id, definition = spec }, ProjectJson), HttpStatusCode.Created);
        Assert.That(replay.GetProperty("version").GetInt64(), Is.EqualTo(2), "Creation replay must not reset current definition");
        Assert.That((await owner.PostAsJsonAsync("/api/v1/projects", new { id, definition = changed }, ProjectJson)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        var history = await ProjectBody(await owner.GetAsync($"/api/v1/projects/{id}/revisions?limit=1"), HttpStatusCode.OK);
        Assert.That(history.GetArrayLength(), Is.EqualTo(1)); Assert.That(history[0].GetProperty("definitionVersion").GetInt64(), Is.EqualTo(2));
        var first = await ProjectBody(await owner.GetAsync($"/api/v1/projects/{id}/revisions?before=2"), HttpStatusCode.OK);
        Assert.That(first[0].GetProperty("definition").GetProperty("budgetAmount").GetDecimal(), Is.EqualTo(5000));
        await using var db = TenantPg(TenantA); Assert.That(await db.OutboxMessages.CountAsync(m => m.Type == "v1.ProjectChanged"), Is.EqualTo(2));
    }
    [Test]
    public async Task ProjectMembersAreRequiredEvenForTenantManagersAndReadOnlyMembersCannotEdit()
    {
        await using var factory = new ProjectApiFactory(_workforceOptions, _fulfillmentOptions);
        using var owner = factory.Actor("owner", TenantA); using var outsider = factory.Actor("manager-without-membership", TenantA);
        using var viewer = factory.Actor("viewer", TenantA, managers: false); using var otherTenant = factory.Actor("owner", TenantB);
        var id = Guid.NewGuid(); var spec = ProjectSpec(); await ProjectBody(await owner.PostAsJsonAsync("/api/v1/projects", new { id, definition = spec }, ProjectJson), HttpStatusCode.Created);
        Assert.That((await outsider.GetAsync($"/api/v1/projects/{id}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await otherTenant.GetAsync($"/api/v1/projects/{id}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await ProjectBody(await outsider.GetAsync("/api/v1/projects"), HttpStatusCode.OK)).GetArrayLength(), Is.Zero);
        await ProjectBody(await ProjectMutation(owner, HttpMethod.Put, $"/api/v1/projects/{id}/members", new { subject = "viewer", role = "Viewer" }, 1), HttpStatusCode.OK);
        await ProjectBody(await viewer.GetAsync($"/api/v1/projects/{id}"), HttpStatusCode.OK);
        var page = await ProjectBody(await viewer.GetAsync("/api/v1/projects"), HttpStatusCode.OK);
        Assert.That(page.GetArrayLength(), Is.EqualTo(1)); Assert.That(page[0].GetProperty("role").GetString(), Is.EqualTo("Viewer"));
        Assert.That(page[0].TryGetProperty("definition", out _), Is.False, "Pages return bounded metadata instead of all graph evidence");
        Assert.That((await ProjectMutation(viewer, HttpMethod.Put, $"/api/v1/projects/{id}/definition", spec with { BudgetAmount = 7000 }, 2)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        await ProjectBody(await ProjectMutation(owner, HttpMethod.Delete, $"/api/v1/projects/{id}/members/viewer", null, 2), HttpStatusCode.OK);
        Assert.That((await viewer.GetAsync($"/api/v1/projects/{id}/revisions")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
    [Test]
    public async Task ProjectPermissionGuardsExistingQuoteAllocationLicenseAndWorkflowEndpoints()
    {
        await using var factory = new ProjectApiFactory(_workforceOptions, _fulfillmentOptions);
        using var owner = factory.Actor("owner", TenantA); using var planner = factory.Actor("planner", TenantA);
        using var approver = factory.Actor("approver", TenantA); using var outsider = factory.Actor("outsider", TenantA);
        var id = Guid.NewGuid(); await ProjectBody(await owner.PostAsJsonAsync("/api/v1/projects", new { id, definition = ProjectSpec() }, ProjectJson), HttpStatusCode.Created);
        await ProjectBody(await ProjectMutation(owner, HttpMethod.Put, $"/api/v1/projects/{id}/members", new { subject = "planner", role = "Planner" }, 1), HttpStatusCode.OK);
        await ProjectBody(await ProjectMutation(owner, HttpMethod.Put, $"/api/v1/projects/{id}/members", new { subject = "approver", role = "Approver" }, 2), HttpStatusCode.OK);
        var resource = await RegisterTenantResource(TenantA);
        var quoteBody = new { projectId = id, from = ProjectStart, to = ProjectStart.AddHours(1), currency = "USD", candidateIds = new[] { resource },
            slots = new[] { new DemandSlot(Guid.NewGuid(), "Lead", ResourceKind.Human, new Dictionary<string, int>()) } };
        Assert.That((await outsider.PostAsJsonAsync("/api/v1/planning/quotes", quoteBody)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var quote = await ProjectBody(await planner.PostAsJsonAsync("/api/v1/planning/quotes", quoteBody), HttpStatusCode.Created);
        var quoteId = quote.GetProperty("id").GetGuid();
        Assert.That((await outsider.GetAsync($"/api/v1/planning/quotes/{quoteId}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var allocate = new { projectId = id, resourceIds = new[] { resource }, periodFrom = ProjectStart, periodTo = ProjectStart.AddHours(1) };
        Assert.That((await planner.PostAsJsonAsync("/api/v1/allocate", allocate)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await outsider.GetAsync($"/api/v1/projects/allocations?ids={id}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await outsider.GetAsync($"/api/v1/audit/allocations/{id}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await outsider.PostAsync($"/api/v1/licenses/compiler/allocate?seats=1&projectId={id}", null)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var workflowId = Guid.NewGuid();
        await ProjectBody(await planner.PostAsJsonAsync("/api/v1/workflows", new { workflowId, quoteId, licenses = Array.Empty<object>() }), HttpStatusCode.Created);
        Assert.That((await ProjectMutation(planner, HttpMethod.Post, $"/api/v1/workflows/{workflowId}/approve", null, 1)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        await ProjectBody(await ProjectMutation(approver, HttpMethod.Post, $"/api/v1/workflows/{workflowId}/approve", null, 1), HttpStatusCode.Accepted);
        Assert.That((await outsider.GetAsync($"/api/v1/workflows/{workflowId}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await ProjectBody(await outsider.GetAsync("/api/v1/workflows"), HttpStatusCode.OK)).GetArrayLength(), Is.Zero);
    }
    [Test]
    public async Task ProductionProjectAccessFailsClosedForUnregisteredLegacyIds()
    {
        await using var factory = new ProjectApiFactory(_workforceOptions, _fulfillmentOptions);
        using var actor = factory.Actor("manager", TenantA);
        var command = new { projectId = Guid.NewGuid(), resourceIds = new[] { Guid.NewGuid() }, periodFrom = ProjectStart, periodTo = ProjectStart.AddHours(1) };
        Assert.That((await actor.PostAsJsonAsync("/api/v1/allocate", command)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        await using var db = TenantSql(TenantA); Assert.That(await db.CommandReceipts.CountAsync(), Is.Zero); Assert.That(await db.Allocations.CountAsync(), Is.Zero);
    }
    [Test]
    public async Task ConcurrentProjectEditsKeepOneVersionAndOneImmutableRevision()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var store = ProjectStore(); var spec = ProjectSpec();
        var id = Guid.NewGuid(); await store.CreateAsync(id, spec, default);
        async Task<bool> Revise(decimal budget)
        {
            try { await ProjectStore().ReplaceAsync(id, 1, spec with { BudgetAmount = budget }, default); return true; }
            catch (ProjectVersionException) { return false; }
        }
        var results = await Task.WhenAll(Revise(6000), Revise(7000)); Assert.That(results.Count(s => s), Is.EqualTo(1));
        var project = (await store.GetAsync(id, default))!; Assert.That(project.Version, Is.EqualTo(2));
        Assert.That((await store.RevisionsAsync(id, null, 50, default))!, Has.Count.EqualTo(2));
        await using var db = TenantPg(TenantA); Assert.That(await db.OutboxMessages.CountAsync(m => m.Type == "v1.ProjectChanged"), Is.EqualTo(2));
    }
    [Test]
    public async Task CreationReplaysAreConcurrentAndAtomicWithProjectEvidenceAndOutbox()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var id = Guid.NewGuid(); var spec = ProjectSpec();
        var results = await Task.WhenAll(ProjectStore().CreateAsync(id, spec, default), ProjectStore().CreateAsync(id, spec, default));
        Assert.That(results.All(p => p.Version == 1), Is.True);
        await using var db = TenantPg(TenantA); Assert.That(await db.Set<OperationalProject>().CountAsync(), Is.EqualTo(1));
        Assert.That(await db.Set<ProjectDefinitionRevision>().CountAsync(), Is.EqualTo(1)); Assert.That(await db.OutboxMessages.CountAsync(), Is.EqualTo(1));
    }
    [Test]
    public async Task RestrictedDatabaseRoleCannotRewriteProjectHistoryOrCrossTenantOwnership()
    {
        Guid id;
        using (TenantExecution.Enter(TenantScope.For(TenantA))) { id = Guid.NewGuid(); await ProjectStore().CreateAsync(id, ProjectSpec(), default); }
        await using (var admin = new FulfillmentContext(_fulfillmentOptions, TenantScope.System))
            await admin.Database.ExecuteSqlRawAsync("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fusionops_project_test') THEN CREATE ROLE fusionops_project_test LOGIN PASSWORD 'FusionOps_test-2026!' NOSUPERUSER NOBYPASSRLS; END IF; END $$; GRANT USAGE ON SCHEMA public TO fusionops_project_test; GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO fusionops_project_test;");
        var connection = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Username = "fusionops_project_test", Password = "FusionOps_test-2026!" };
        var options = new DbContextOptionsBuilder<FulfillmentContext>().UseNpgsql(connection.ConnectionString).Options;
        await using (var b = new FulfillmentContext(options, TenantScope.For(TenantB)))
        {
            Assert.That(await b.Set<OperationalProject>().IgnoreQueryFilters().CountAsync(), Is.Zero);
            Assert.That(await b.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM ops_project_members").SingleAsync(), Is.Zero);
            Assert.That(await b.Set<ProjectDefinitionRevision>().IgnoreQueryFilters().CountAsync(), Is.Zero);
            var error = Assert.ThrowsAsync<PostgresException>(() => b.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ops_project_members (\"ProjectId\", \"TenantId\", \"Subject\", \"Role\") VALUES ({id}, {TenantB}, 'intruder', 'Owner')"));
            Assert.That(error!.SqlState, Is.EqualTo("23503"));
        }
        await using (var a = new FulfillmentContext(options, TenantScope.For(TenantA)))
        {
            Assert.That(await a.Database.ExecuteSqlInterpolatedAsync($"UPDATE ops_project_revisions SET \"Digest\" = 'changed' WHERE \"ProjectId\" = {id}"), Is.Zero);
            Assert.That(await a.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ops_project_revisions WHERE \"ProjectId\" = {id}"), Is.Zero);
            var revision = await a.Set<ProjectDefinitionRevision>().SingleAsync(); revision.VerifyEvidence();
            a.Entry(revision).Property(r => r.Digest).CurrentValue = "changed";
            Assert.That(Assert.ThrowsAsync<ProjectRuleException>(() => a.SaveChangesAsync())!.Code, Is.EqualTo("project_revision_immutable"));
        }
    }
    [Test]
    public async Task InvalidGraphRevisionAndDeniedMembershipChangeHaveNoPersistedEffects()
    {
        using var tenant = TenantExecution.Enter(TenantScope.For(TenantA)); var spec = ProjectSpec(); var id = Guid.NewGuid(); var store = ProjectStore();
        await store.CreateAsync(id, spec, default);
        var cycle = spec with { Tasks = [spec.Tasks[0] with { Dependencies = [spec.Tasks[1].Id] }, spec.Tasks[1]] };
        Assert.That(Assert.ThrowsAsync<ProjectRuleException>(() => store.ReplaceAsync(id, 1, cycle, default))!.Code, Is.EqualTo("project_dependency_cycle"));
        Assert.ThrowsAsync<ProjectAccessException>(() => ProjectStore("intruder").SetMemberAsync(id, 1, "intruder", ProjectRole.Planner, default));
        await using var db = TenantPg(TenantA); Assert.That((await db.Set<OperationalProject>().SingleAsync()).Version, Is.EqualTo(1));
        Assert.That(await db.Set<ProjectDefinitionRevision>().CountAsync(), Is.EqualTo(1)); Assert.That(await db.OutboxMessages.CountAsync(), Is.EqualTo(1));
    }
}
