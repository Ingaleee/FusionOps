using System;

namespace FusionOps.Domain.Shared.Interfaces;

/// <summary>
/// Marker interface for domain events used for decoupled communication inside the domain.
/// </summary>
public interface IDomainEvent
{
    Guid Id { get; }
    DateTimeOffset OccurredOn { get; }
} 