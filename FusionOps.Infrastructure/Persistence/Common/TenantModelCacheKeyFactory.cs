using FusionOps.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FusionOps.Infrastructure.Persistence.Common;

public sealed class TenantModelCacheKeyFactory : IModelCacheKeyFactory
{
    private readonly ITenantProvider _tenantProvider;
    private readonly Microsoft.Extensions.Options.IOptionsMonitor<TenancyOptions> _options;

    public TenantModelCacheKeyFactory(ITenantProvider tenantProvider, Microsoft.Extensions.Options.IOptionsMonitor<TenancyOptions> options)
    {
        _tenantProvider = tenantProvider;
        _options = options;
    }

    public object Create(DbContext context, bool designTime)
    {
        var mode = _options.CurrentValue.Mode;
        var key = mode.Equals("Schema", StringComparison.OrdinalIgnoreCase) && _tenantProvider.IsSet
            ? _tenantProvider.TenantId
            : "_";
        return (context.GetType(), designTime, mode, key);
    }
}


