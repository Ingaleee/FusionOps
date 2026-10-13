using System.Globalization;
using FusionOps.Application.Projects;
using FusionOps.Domain.Projects;

namespace FusionOps.Presentation.Modules;

public static class OperationalProjectEndpoints
{
    public static IEndpointRouteBuilder MapOperationalProjectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/api/v1/projects").WithTags("Project commitments");
        var development = endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment();
        if (!development) routes.RequireAuthorization();
        var create = routes.MapPost("/", async (CreateProjectDto dto, IProjectStore store, HttpResponse response, CancellationToken ct) =>
        {
            var project = await store.CreateAsync(dto.Id, dto.Definition, ct); SetETag(response, project);
            return Results.Created($"/api/v1/projects/{project.Id:D}", View(project));
        });
        if (!development) create.RequireAuthorization("ManageResources");
        routes.MapGet("/", async (Guid? after, int? limit, IProjectStore store, HttpResponse response, CancellationToken ct) =>
        {
            response.Headers.CacheControl = "no-store";
            return Results.Ok(await store.ListAsync(after, limit ?? 20, ct));
        });
        routes.MapGet("/{id:guid}", async (Guid id, IProjectStore store, HttpResponse response, CancellationToken ct) =>
        {
            var project = await store.GetAsync(id, ct); if (project is null) return Results.NotFound();
            SetETag(response, project); return Results.Ok(View(project));
        });
        routes.MapPut("/{id:guid}/definition", async (Guid id, ProjectDefinition definition, HttpRequest request, HttpResponse response,
            IProjectStore store, CancellationToken ct) => Result(await store.ReplaceAsync(id, Version(request), definition, ct), response));
        routes.MapPut("/{id:guid}/members", async (Guid id, MemberDto dto, HttpRequest request, HttpResponse response,
            IProjectStore store, CancellationToken ct) => Result(await store.SetMemberAsync(id, Version(request), dto.Subject, dto.Role, ct), response));
        routes.MapDelete("/{id:guid}/members/{subject}", async (Guid id, string subject, HttpRequest request, HttpResponse response,
            IProjectStore store, CancellationToken ct) => Result(await store.RemoveMemberAsync(id, Version(request), subject, ct), response));
        routes.MapPost("/{id:guid}/owner", async (Guid id, OwnerDto dto, HttpRequest request, HttpResponse response,
            IProjectStore store, CancellationToken ct) => Result(await store.TransferOwnerAsync(id, Version(request), dto.Subject, ct), response));
        routes.MapGet("/{id:guid}/revisions", async (Guid id, long? before, int? limit, IProjectStore store, HttpResponse response, CancellationToken ct) =>
        {
            var revisions = await store.RevisionsAsync(id, before, limit ?? 20, ct); response.Headers.CacheControl = "no-store";
            return revisions is null ? Results.NotFound() : Results.Ok(revisions.Select(r => new
            { r.ProjectId, r.DefinitionVersion, r.ProjectVersion, r.Digest, r.Actor, r.RecordedAt, r.Definition }));
        });
        return endpoints;
    }
    private static IResult Result(OperationalProject? project, HttpResponse response)
    {
        if (project is null) return Results.NotFound(); SetETag(response, project); return Results.Ok(View(project));
    }
    private static object View(OperationalProject p) => new { p.Id, p.Version, p.DefinitionVersion, p.DefinitionDigest, SchemaVersion = ProjectDefinitionCodec.SchemaVersion,
        p.CreatedBy, p.CreatedAt, p.UpdatedAt, p.Definition, p.Members, Analysis = ProjectDefinitionCodec.Analyze(p.Definition) };
    private static void SetETag(HttpResponse response, OperationalProject project)
    {
        response.Headers.ETag = $"\"{project.Version.ToString(CultureInfo.InvariantCulture)}\""; response.Headers.CacheControl = "no-store";
    }
    private static long Version(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("If-Match", out var values)) throw new ProjectPreconditionRequiredException();
        var text = values.ToString();
        if (values.Count != 1 || text.Length < 3 || text[0] != '"' || text[^1] != '"'
            || !long.TryParse(text.AsSpan(1, text.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 1)
            throw new ArgumentException("If-Match must contain one quoted positive project version");
        return value;
    }
    public sealed record CreateProjectDto(Guid Id, ProjectDefinition Definition);
    public sealed record MemberDto(string Subject, ProjectRole Role);
    public sealed record OwnerDto(string Subject);
}
public sealed class ProjectPreconditionRequiredException : Exception;
