namespace FusionOps.Application.Abstractions;

public interface IResourceNotification
{
    Task LowStockAlertAsync(string sku, DateTime expectedDate, int qtyLeft, CancellationToken ct);
    Task AllocationUpdateAsync(Guid projectId, string status, CancellationToken ct);
} 