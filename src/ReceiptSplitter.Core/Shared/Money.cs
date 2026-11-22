namespace ReceiptSplitter.Core.Shared;

/// <summary>
/// Money is always an integer count of the currency's minor unit (centavos for PHP, yen for JPY).
/// </summary>
public readonly record struct Money(long Minor, string Currency);

/// <summary>An ISO 4217 currency and its minor-unit digits (0, 2 or 3).</summary>
public readonly record struct Currency
{
    public Currency(string code, int digits)
    {
        if (digits is not (0 or 2 or 3))
            throw new ArgumentOutOfRangeException(nameof(digits), digits, "Minor-unit digits must be 0, 2 or 3.");
        Code = code;
        Digits = digits;
    }

    public string Code { get; }
    public int Digits { get; }

    private long Scale => Digits switch { 0 => 1, 2 => 100, _ => 1000 };

    /// <summary>Parses input. Refuses amounts finer than the minor unit instead of rounding them.</summary>
    public long ToMinor(decimal amount)
    {
        var scaled = amount * Scale;
        if (scaled != decimal.Truncate(scaled))
            throw new ArgumentException($"{amount} has more than {Digits} decimal places for {Code}.", nameof(amount));
        return decimal.ToInt64(scaled);
    }

    /// <summary>For display only. Never do maths on the result.</summary>
    public decimal ToDecimal(long minor) => decimal.Divide(minor, Scale);
}
