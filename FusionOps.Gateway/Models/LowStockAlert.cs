using System;

namespace FusionOps.Gateway.Models;

public class LowStockAlert
{
    public string Sku { get; set; } = default!;
    public int QtyLeft { get; set; }
    public DateTime ExpectedDate { get; set; }
}
