using System.CommandLine;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FusionOps.Cli.Commands;

public class AllocateCommand : Command
{
    private readonly IServiceProvider _serviceProvider;

    public AllocateCommand(IServiceProvider serviceProvider) : base("allocate", "Allocate resources for a task")
    {
        _serviceProvider = serviceProvider;

        var warehouseIdOption = new Option<string>("--warehouse-id", "Warehouse ID") { IsRequired = true };
        var equipmentTypeOption = new Option<string>("--equipment-type", "Equipment type (Forklift, Crane, Conveyor)") { IsRequired = true };
        var startTimeOption = new Option<DateTime>("--start-time", "Start time (ISO format)") { IsRequired = true };
        var durationOption = new Option<int>("--duration-hours", "Duration in hours") { IsRequired = true };
        var priorityOption = new Option<int>("--priority", "Priority (1-10)") { IsRequired = true };

        AddOption(warehouseIdOption);
        AddOption(equipmentTypeOption);
        AddOption(startTimeOption);
        AddOption(durationOption);
        AddOption(priorityOption);

        this.SetHandler(async (warehouseId, equipmentType, startTime, duration, priority) =>
        {
            await HandleAllocate(warehouseId, equipmentType, startTime, duration, priority);
        }, warehouseIdOption, equipmentTypeOption, startTimeOption, durationOption, priorityOption);
    }

    private async Task HandleAllocate(string warehouseId, string equipmentType, DateTime startTime, int duration, int priority)
    {
        var logger = _serviceProvider.GetRequiredService<ILogger<AllocateCommand>>();
        var httpClient = _serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();

        try
        {
            var request = new
            {
                WarehouseId = warehouseId,
                EquipmentType = equipmentType,
                StartTime = startTime,
                DurationHours = duration,
                Priority = priority
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync("http://localhost:5000/api/workforce/allocate", content);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                logger.LogInformation("Allocation created successfully: {Result}", result);
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                logger.LogError("Failed to create allocation: {Error}", error);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating allocation");
        }
    }
} 