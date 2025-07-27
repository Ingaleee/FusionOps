using System;

namespace FusionOps.Domain.Shared;

/// <summary>
/// Represents a business-rule violation surfaced from the domain layer.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}