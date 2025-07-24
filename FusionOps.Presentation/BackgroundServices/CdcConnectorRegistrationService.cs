using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FusionOps.Presentation.BackgroundServices;

public class CdcConnectorRegistrationService : BackgroundService
{
    private readonly ILogger<CdcConnectorRegistrationService> _log;
    private readonly IHttpClientFactory _factory;
    private readonly IConfiguration _cfg;

    public CdcConnectorRegistrationService(ILogger<CdcConnectorRegistrationService> log, IHttpClientFactory factory, IConfiguration cfg)
    {
        _log = log;
        _factory = factory;
        _cfg = cfg;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectUrl = _cfg["Kafka:ConnectUrl"] ?? "http://connect:8083";
        var client = _factory.CreateClient("debezium");
        client.BaseAddress = new Uri(connectUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var list = await client.GetFromJsonAsync<List<string>>("/connectors", cancellationToken: stoppingToken);
                if (list != null && list.Contains("sqlserver-allocations"))
                {
                    _log.LogInformation("Debezium connector already present");
                    return;
                }

                var payload = new
                {
                    name = "sqlserver-allocations",
                    config = new Dictionary<string, string>
                    {
                        ["connector.class"] = "io.debezium.connector.sqlserver.SqlServerConnector",
                        ["database.hostname"] = "sqlserver",
                        ["database.port"] = "1433",
                        ["database.user"] = "sa",
                        ["database.password"] = "Pass@word1",
                        ["database.names"] = "FusionOpsWorkforce",
                        ["table.include.list"] = "dbo.Allocations",
                        ["topic.prefix"] = "dbserver1",
                        ["schema.history.internal.kafka.bootstrap.servers"] = "kafka:9092",
                        ["schema.history.internal.kafka.topic"] = "schema-changes.sqlserver",
                        ["snapshot.mode"] = "schema_only"
                    }
                };

                var response = await client.PostAsJsonAsync("/connectors", payload, stoppingToken);
                if (response.IsSuccessStatusCode)
                {
                    _log.LogInformation("Debezium connector registered");
                    return;
                }
                _log.LogWarning("Failed to register connector: {Status}", response.StatusCode);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Waiting for Kafka Connect service...");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
} 