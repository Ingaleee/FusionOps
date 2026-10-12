using System.Globalization;
using FusionOps.Application.Planning;
using FusionOps.Domain.Catalog;
using FusionOps.Domain.Enumerations;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.Shared;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Presentation.Modules;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/api/v1/catalog/resources").WithTags("Resource catalog");
        if (!endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment()) routes.RequireAuthorization("ManageResources");
        routes.MapPost("/", async (RegisterDto dto, IResourceCatalogStore store, HttpResponse response, CancellationToken ct) =>
        {
            var resource = await store.RegisterAsync(dto.Id, dto.Kind, Edit(dto.Name, dto.HourlyRate, dto.Currency, dto.Skills, dto.EquipmentType, true), ct);
            SetETag(response, resource);
            return Results.Created($"/api/v1/catalog/resources/{resource.Id:D}", View(resource));
        });
        routes.MapGet("/{id:guid}", async (Guid id, IResourceCatalogStore store, HttpResponse response, CancellationToken ct) =>
        {
            var resource = await store.GetAsync(id, ct);
            if (resource is null) return Results.NotFound();
            SetETag(response, resource);
            return Results.Ok(View(resource));
        });
        routes.MapGet("/", async (Guid? after, int? limit, IResourceCatalogStore store, CancellationToken ct) =>
            Results.Ok((await store.ListAsync(after, limit ?? 20, ct)).Select(View)));
        routes.MapPut("/{id:guid}", async (Guid id, ReviseDto dto, IResourceCatalogStore store, HttpRequest request, HttpResponse response, CancellationToken ct) =>
        {
            if (!request.Headers.TryGetValue("If-Match", out var values)) return Results.Problem(statusCode: 428, title: "Supply the resource ETag in If-Match", type: "precondition_required");
            var text = values.ToString();
            if (values.Count != 1 || text.Length < 3 || text[0] != '"' || text[^1] != '"'
                || !long.TryParse(text.AsSpan(1, text.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1)
                return Results.Problem(statusCode: 400, title: "If-Match must contain one quoted positive revision", type: "invalid_precondition");
            var resource = await store.ReviseAsync(id, revision, Edit(dto.Name, dto.HourlyRate, dto.Currency, dto.Skills, dto.EquipmentType, dto.IsActive), ct);
            if (resource is null) return Results.NotFound();
            SetETag(response, resource);
            return Results.Ok(View(resource));
        });
        return endpoints;
    }
    public static Currency ParseCurrency(string text) => Currency.All.SingleOrDefault(c => string.Equals(c.Name, text, StringComparison.OrdinalIgnoreCase)) is var currency
        && currency != default ? currency : throw new DomainException("Use USD, EUR or RUB");
    private static CatalogEdit Edit(string name, decimal rate, string currency, IReadOnlyDictionary<string, int> skills, string? type, bool active) =>
        new(name, new Money(rate, ParseCurrency(currency)), skills, type, active);
    private static void SetETag(HttpResponse response, CatalogResource r)
    {
        response.Headers.ETag = $"\"{r.Revision.ToString(CultureInfo.InvariantCulture)}\"";
        response.Headers.CacheControl = "no-store";
    }
    private static object View(CatalogResource r) => new { r.Id, r.Name, r.Kind, HourlyRate = r.HourRate.Amount, Currency = r.HourRate.Currency.Name, r.Skills, r.EquipmentType, r.IsActive, r.Revision };
    public sealed record RegisterDto(Guid Id, string Name, ResourceKind Kind, decimal HourlyRate, string Currency,
        IReadOnlyDictionary<string, int> Skills, string? EquipmentType = null);
    public sealed record ReviseDto(string Name, decimal HourlyRate, string Currency, IReadOnlyDictionary<string, int> Skills,
        string? EquipmentType, bool IsActive);
}
