# FusionOps CLI

Command-line interface for FusionOps resource management system.

## Installation

### As Global Tool

```bash
# Build and install globally
dotnet tool install --global --add-source ./nupkg FusionOps.Cli

# Or install from local build
dotnet pack
dotnet tool install --global --add-source ./FusionOps.Cli/bin/Debug FusionOps.Cli
```

### Development

```bash
# Run directly from source
dotnet run --project FusionOps.Cli

# Build
dotnet build FusionOps.Cli
```

## Usage

### Allocate Resources

Allocate equipment resources for a specific task:

```bash
dotnet-fusion allocate \
  --warehouse-id "wh-001" \
  --equipment-type "Forklift" \
  --start-time "2024-01-15T08:00:00Z" \
  --duration-hours 4 \
  --priority 5
```

**Parameters:**
- `--warehouse-id`: Target warehouse ID
- `--equipment-type`: Equipment type (Forklift, Crane, Conveyor)
- `--start-time`: Start time in ISO format
- `--duration-hours`: Duration in hours
- `--priority`: Priority level (1-10)

### Stock Forecast

Get stock forecast for a warehouse:

```bash
dotnet-fusion stock forecast \
  --warehouse-id "wh-001" \
  --days 30
```

**Parameters:**
- `--warehouse-id`: Warehouse ID to forecast
- `--days`: Number of days to forecast

### Real-time Notifications

Connect to SignalR hub for real-time notifications:

```bash
dotnet-fusion notify connect --hub-url "http://localhost:5000/notificationHub"
```

**Parameters:**
- `--hub-url`: SignalR hub URL (default: http://localhost:5000/notificationHub)

**Supported Events:**
- `AllocationUpdate`: Resource allocation updates
- `LowStock`: Low stock alerts
- `ResourceAllocated`: Resource allocation confirmations
- `StockReplenished`: Stock replenishment notifications

## Examples

### Complete Workflow

1. **Get stock forecast:**
   ```bash
   dotnet-fusion stock forecast --warehouse-id "wh-001" --days 7
   ```

2. **Allocate resources:**
   ```bash
   dotnet-fusion allocate \
     --warehouse-id "wh-001" \
     --equipment-type "Forklift" \
     --start-time "2024-01-15T08:00:00Z" \
     --duration-hours 4 \
     --priority 5
   ```

3. **Monitor real-time updates:**
   ```bash
   dotnet-fusion notify connect
   ```

### Batch Operations

```bash
# Allocate multiple resources
for i in {1..5}; do
  dotnet-fusion allocate \
    --warehouse-id "wh-001" \
    --equipment-type "Forklift" \
    --start-time "2024-01-15T$((8+i)):00:00Z" \
    --duration-hours 2 \
    --priority $((5+i))
done
```

## Configuration

The CLI connects to the FusionOps API running on `http://localhost:5000` by default.

To change the API endpoint, set the `FUSIONOPS_API_URL` environment variable:

```bash
export FUSIONOPS_API_URL="https://api.fusionops.com"
dotnet-fusion stock forecast --warehouse-id "wh-001" --days 30
```

## Development

### Project Structure

```
FusionOps.Cli/
├── Commands/
│   ├── AllocateCommand.cs      # Resource allocation
│   ├── StockCommand.cs         # Stock operations
│   ├── StockForecastCommand.cs # Stock forecasting
│   ├── NotifyCommand.cs        # Notification operations
│   └── NotifyConnectCommand.cs # SignalR connection
├── Program.cs                  # Entry point
└── FusionOps.Cli.csproj       # Project file
```

### Adding New Commands

1. Create a new command class inheriting from `Command`
2. Register it in `Program.cs`
3. Add documentation to this README

### Testing

```bash
# Test individual commands
dotnet run --project FusionOps.Cli allocate --help
dotnet run --project FusionOps.Cli stock forecast --help
dotnet run --project FusionOps.Cli notify connect --help
```

## Troubleshooting

### Connection Issues

- Ensure the FusionOps API is running on the expected URL
- Check firewall settings
- Verify network connectivity

### SignalR Connection

- Ensure the SignalR hub is accessible
- Check CORS settings on the server
- Verify the hub URL is correct

### Authentication

Currently, the CLI doesn't support authentication. For production use, consider adding JWT token support. 