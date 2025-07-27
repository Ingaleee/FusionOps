using System;
using FusionOps.Domain.Enumerations;

namespace FusionOps.Domain.ValueObjects;

/// <summary>
/// Monetary value coupled with its currency.
/// </summary>
public readonly record struct Money(decimal Amount, Currency Currency)
{
    public static Money Of(decimal amount, Currency currency)
    {
        if (amount < 0)
            throw new ArgumentException("Amount cannot be negative", nameof(amount));
        return new Money(amount, currency);
    }

    public static Money Usd(decimal amount) => Of(amount, Currency.USD);

    public static Money Eur(decimal amount) => Of(amount, Currency.EUR);

    public static Money Rub(decimal amount) => Of(amount, Currency.RUB);

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator *(Money money, decimal multiplier) => new(money.Amount * multiplier, money.Currency);

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw new InvalidOperationException("Money operations require matching currency");
    }

    public override string ToString() => $"{Amount} {Currency.Name}";
}