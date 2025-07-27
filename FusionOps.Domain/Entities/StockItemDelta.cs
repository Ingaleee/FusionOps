namespace FusionOps.Domain.Entities;

/// <summary>
/// DTO для массового пополнения запасов.
/// </summary>
public record StockItemDelta(string Sku, int QuantityDelta);