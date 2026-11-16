namespace ReceiptSplitter.Core;

/// <summary>
/// Money is always an integer count of the currency's minor unit (centavos for PHP, yen for JPY).
/// Module 1 adds allocation, receipt maths and settle-up here. See docs/SPEC.md section 6.
/// </summary>
public readonly record struct Money(long Minor, string Currency);
