using System;

namespace FusionOps.Domain.Shared;

/// <summary>
/// Static helper holding guard clauses for argument validation within the domain layer.
/// </summary>
public static class Guard
{
    public static T AgainstNull<T>(T? input, string paramName) where T : class
    {
        if (input is null)
            throw new DomainException($"{paramName} cannot be null.");
        return input;
    }

    public static string AgainstNullOrWhiteSpace(string? input, string paramName)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new DomainException($"{paramName} cannot be null or whitespace.");
        return input!;
    }

    public static int AgainstNegative(int input, string paramName)
    {
        if (input < 0)
            throw new DomainException($"{paramName} cannot be negative.");
        return input;
    }

    public static decimal AgainstNegative(decimal input, string paramName)
    {
        if (input < 0)
            throw new DomainException($"{paramName} cannot be negative.");
        return input;
    }
}