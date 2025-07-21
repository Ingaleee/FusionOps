using System;

namespace FusionOps.Domain.Interfaces;

public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
} 