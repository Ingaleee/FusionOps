namespace FusionOps.Domain.Interfaces;

using System.Threading.Tasks;
using FusionOps.Domain.Entities;

public interface IStockForecaster
{
    /// <summary>
    /// Предсказывает потребность (кол-во штук) в течение указанного горизонта (days) для заданного SKU.
    /// </summary>
    /// <param name="item">Складская позиция.</param>
    /// <param name="days">Горизонт прогноза в днях.</param>
    /// <returns>Ожидаемое количество, которое потребуется в течение периода.</returns>
    Task<float> ForecastAsync(StockItem item, int days);
}