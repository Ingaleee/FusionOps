/// <summary>
/// Interface implemented by rich enumerations (smart enums).
/// </summary>
namespace FusionOps.Domain.Shared.Interfaces;

public interface IEnumeration
{
    int Value { get; }
    string Name { get; }
} 