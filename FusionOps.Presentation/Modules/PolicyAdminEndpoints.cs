using FusionOps.Application.Policies;
using FusionOps.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Presentation.Modules;

public static class PolicyAdminEndpoints
{
    public static IEndpointRouteBuilder MapPolicyAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/policy").RequireAuthorization("T:AdminStock"); // placeholder policy

        g.MapPost("/{name}/draft", async (string name, string engine, string source, PolicyContext ctx) =>
        {
            var doc = new PolicyDocument
            {
                Id = Guid.NewGuid(),
                TenantId = "_", // actual value is set via RLS shadow or middleware in real impl
                Name = name,
                Engine = engine,
                Version = 1,
                Status = "draft",
                Source = source,
                CreatedAt = DateTimeOffset.UtcNow
            };
            ctx.PolicyDocuments.Add(doc);
            await ctx.SaveChangesAsync();
            return Results.Ok(new { doc.Id });
        });

        g.MapPost("/{name}/activate", async (string name, PolicyContext ctx) =>
        {
            var docs = await ctx.PolicyDocuments.Where(x => x.Name == name).ToListAsync();
            foreach (var d in docs) d.Status = d.Status == "draft" ? "active" : d.Status;
            await ctx.SaveChangesAsync();
            return Results.Ok(new { name });
        });

        g.MapPost("/{name}/dry-run", async (string name, PolicyInput input, IPolicyEngine engine, CancellationToken ct) =>
        {
            var decision = await engine.EvaluateAsync(name, input, ct);
            return Results.Ok(decision);
        });

        return app;
    }
}



