using System.Linq.Expressions;
using FusionOps.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Persistence.Common;

public static class TenancyModelBuilderExtensions
{
    public static void AddTenantShadowProperty(this ModelBuilder modelBuilder, string propertyName = "TenantId")
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.IsOwned()) continue;
            if (entity.FindProperty(propertyName) is null)
            {
                entity.AddProperty(propertyName, typeof(string));
                var prop = entity.FindProperty(propertyName)!;
                entity.AddIndex(prop);
            }
        }
    }

    public static void AddGlobalTenantFilter(this ModelBuilder modelBuilder, ITenantProvider tenantProvider)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.IsOwned()) continue;
            var parameter = Expression.Parameter(entity.ClrType, "e");
            var tenantProperty = Expression.Call(
                typeof(EF), nameof(EF.Property), new[] { typeof(string) }, parameter, Expression.Constant("TenantId"));
            var equals = Expression.Equal(tenantProperty, Expression.Constant(tenantProvider.TenantId));
            var lambda = Expression.Lambda(equals, parameter);
            entity.SetQueryFilter(lambda);
        }
    }
}



