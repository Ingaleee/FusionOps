using System.Threading.Tasks;
using Xunit;
using Testcontainers.EventStoreDb;
using Testcontainers.PostgreSql;
using Npgsql;
using EventStore.Client;

public class AllocateToHistoryE2ETest : IAsyncLifetime
{
    private readonly EventStoreDbContainer _eventStore = new EventStoreDbBuilder().Build();
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithDatabase("FusionOps").WithUsername("sa").WithPassword("yourStrong(!)Password").Build();

    public async Task InitializeAsync()
    {
        await _eventStore.StartAsync();
        await _pg.StartAsync();
        // TODO: Применить миграции для allocation_history (можно через NpgsqlConnection)
        // TODO: Запустить projector-сервис с connection string к _pg и EventStoreDB
        // TODO: Запустить приложение с connection string к EventStoreDB
    }

    public async Task DisposeAsync()
    {
        await _eventStore.DisposeAsync();
        await _pg.DisposeAsync();
    }

    [Fact]
    public async Task Allocate_ProjectsToHistoryTable()
    {
        // TODO: Сделать POST /allocate (через HttpClient или WebApplicationFactory)

        // Ждем projector (можно добавить Task.Delay(5000) для простоты)
        await Task.Delay(5000);

        // Проверяем, что строка появилась в allocation_history
        await using var conn = new NpgsqlConnection(_pg.GetConnectionString());
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM allocation_history";
        var count = (long)await cmd.ExecuteScalarAsync();

        Assert.True(count > 0, "No rows in allocation_history after allocate");
    }
}