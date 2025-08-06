using System.Threading.Tasks;
using Xunit;
using Testcontainers.EventStoreDb;
using EventStore.Client;

public class AllocateToEventStoreTest : IAsyncLifetime
{
    private readonly EventStoreDbContainer _eventStore = new EventStoreDbBuilder().Build();

    public async Task InitializeAsync() => await _eventStore.StartAsync();
    public async Task DisposeAsync() => await _eventStore.DisposeAsync();

    [Fact]
    public async Task Allocate_CreatesEventInEventStore()
    {
        // Arrange: стартуем EventStoreDB и подключаемся к нему
        var settings = EventStoreClientSettings.Create(_eventStore.GetConnectionString());
        var esClient = new EventStoreClient(settings);

        // TODO: Запусти приложение с connection string к _eventStore (например, через переменную окружения)
        // TODO: Сделай POST /allocate (используй WebApplicationFactory или HttpClient)

        // Act: ждем появления события в EventStoreDB
        var events = esClient.ReadAllAsync(Direction.Forwards, Position.Start);
        bool found = false;
        await foreach (var ev in events)
        {
            if (ev.Event.EventType.Contains("ResourceAllocated"))
            {
                found = true;
                break;
            }
        }

        // Assert
        Assert.True(found, "ResourceAllocated event not found in EventStoreDB");
    }
}