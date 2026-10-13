using System.Security.Claims;
using System.Text.Json;

namespace FusionOps.Presentation.Authorization;

public static class KeycloakRoleClaims
{
    public static void Apply(ClaimsPrincipal principal, string audience)
    {
        if (principal.Identity is not ClaimsIdentity identity) return;
        if (principal.FindFirst("realm_access") is { } realm)
        {
            using var document = JsonDocument.Parse(realm.Value);
            AddRoles(identity, document.RootElement);
        }
        if (principal.FindFirst("resource_access") is { } access)
        {
            using var document = JsonDocument.Parse(access.Value);
            if (document.RootElement.TryGetProperty(audience, out var client)) AddRoles(identity, client);
        }
    }

    private static void AddRoles(ClaimsIdentity identity, JsonElement container)
    {
        if (!container.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array) return;
        foreach (var element in roles.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String) continue;
            var role = element.GetString();
            if (!string.IsNullOrWhiteSpace(role) && role.Length <= 128 && !identity.HasClaim(identity.RoleClaimType, role))
                identity.AddClaim(new Claim(identity.RoleClaimType, role));
        }
    }
}
