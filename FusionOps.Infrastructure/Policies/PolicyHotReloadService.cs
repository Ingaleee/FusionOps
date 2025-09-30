using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FusionOps.Infrastructure.Policies;

public sealed class PolicyHotReloadService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PolicyHotReloadService> _logger;
    private readonly string _connStr;

    public PolicyHotReloadService(IServiceScopeFactory scopeFactory, ILogger<PolicyHotReloadService> logger, IConfiguration cfg)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _connStr = cfg.GetConnectionString("pg") ?? string.Empty;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_connStr)) return;
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(stoppingToken);
        conn.Notification += async (_, e) =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                // TODO: fetch active policies for tenant/name from payload and refresh caches
                _ = scope; // placeholder to avoid warnings
                _logger.LogInformation("policy_changed: {Payload}", e.Payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Policy hot-reload failed");
            }
        };
        await using (var cmd = new NpgsqlCommand("LISTEN policy_changed;", conn))
            await cmd.ExecuteNonQueryAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await conn.WaitAsync(stoppingToken);
        }
    }
}


