namespace FusionOps.Domain.Shared.Interfaces;

/// <summary>
/// Generic marker interface for domain entities with strongly typed identifier.
/// </summary>
public interface IEntity<out TId>
{
    TId Id { get; }
}