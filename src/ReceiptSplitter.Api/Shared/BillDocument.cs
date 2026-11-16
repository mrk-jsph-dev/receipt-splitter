using ReceiptSplitter.Api.Infrastructure.Security;
using ReceiptSplitter.Core.Shared;

namespace ReceiptSplitter.Api.Shared;

// The stored bill: one JSON blob per bill. Mirrors docs/SPEC.md "Data model".
// Money is in integer minor units. Only the API reads and writes this shape; responses use BillView.

public enum BillStatus { Draft, Shared, Locked }

public enum ReceiptSource { Sample, Manual, Scan }

public enum AdjustmentSource { Scan, Manual }

public enum PaymentStatus { Waiting, Confirmed }

public enum ActivityKind
{
    AddPerson, Join, Rename, RemovePerson, Claim, Unclaim, SetWeight, MarkPaid, UnmarkPaid,
    AddReceipt, EditReceipt, RemoveReceipt, ClaimRemoved, Settings, Share, Lock, Confirm, Undo, SetCard, Decline,
}

public sealed record CurrencyInfo(string Code, int Digits)
{
    /// <summary>Three upper-case letters (ISO 4217) and 0, 2 or 3 minor-unit digits.</summary>
    public bool IsValid() => Code is { Length: 3 } && Code.All(char.IsAsciiLetterUpper) && Digits is 0 or 2 or 3;
}

public sealed class BillDocument
{
    public required string Id { get; set; }
    public int Schema { get; set; } = 1;
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string Title { get; set; } = "";
    public required CurrencyInfo Currency { get; set; }
    public BillStatus Status { get; set; }
    public List<PersonDoc> People { get; set; } = [];
    public int? HeadcountHint { get; set; }
    public List<ReceiptDoc> Receipts { get; set; } = [];
    public string? PaymentNote { get; set; }
    public List<PaymentDoc> Payments { get; set; } = [];
    public List<ActivityDoc> Activity { get; set; } = [];
    public DateTimeOffset? LockedAt { get; set; }
    public required string OrganizerKeyHash { get; set; }

    public PersonDoc? FindPerson(string id) => People.Find(p => p.Id == id);

    public (ReceiptDoc Receipt, LineDoc Line, UnitDoc Unit)? FindUnit(string unitId)
    {
        foreach (var receipt in Receipts)
            foreach (var line in receipt.Lines)
                foreach (var unit in line.Units)
                    if (unit.Id == unitId)
                        return (receipt, line, unit);
        return null;
    }

    /// <summary>Adds an activity entry. The oldest entries past the limit are dropped on save.</summary>
    public void Log(DateTimeOffset at, ActivityKind kind, string text, string? personId = null, string? unitId = null) =>
        Activity.Add(new ActivityDoc(SecureIds.NewShort(), at, kind, text, personId, unitId));

    public string NameOf(string personId) => FindPerson(personId)?.Name ?? "Someone";

    /// <summary>True when a name is already on the bill, ignoring case and outer spaces.</summary>
    public bool HasName(string name, string? exceptPersonId = null) =>
        People.Any(p => p.Id != exceptPersonId && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed class PersonDoc
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public Card Card { get; set; }
}

public sealed class ReceiptDoc
{
    public required string Id { get; set; }
    public string Label { get; set; } = "";
    public string? Merchant { get; set; }
    public DateOnly? ReceiptDate { get; set; }
    public ReceiptSource Source { get; set; }
    public int Photos { get; set; }
    public long ConfirmedTotal { get; set; }
    public List<LineDoc> Lines { get; set; } = [];
    public List<AdjustmentDoc> Adjustments { get; set; } = [];
    public SplitMode SplitMode { get; set; }

    /// <summary>Split mode "shares": a couple counts as 2, a kid as 0.5. Up to 2 decimal places.</summary>
    public Dictionary<string, decimal>? Shares { get; set; }

    /// <summary>Who was there. Empty means everyone on the bill, including people who join later.</summary>
    public List<string> Participants { get; set; } = [];

    public List<PayerDoc> Payers { get; set; } = [];
}

public sealed class LineDoc
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public int Quantity { get; set; }
    public long UnitPrice { get; set; }
    public long LineTotal { get; set; }
    public decimal? Confidence { get; set; }
    public List<UnitDoc> Units { get; set; } = [];
}

public sealed class UnitDoc
{
    public required string Id { get; set; }
    public List<ClaimDoc> Claims { get; set; } = [];
}

public sealed record ClaimDoc(string PersonId, long Weight = 1);

public sealed class AdjustmentDoc
{
    public required string Id { get; set; }
    public AdjustmentKind Kind { get; set; }
    public string Label { get; set; } = "";
    public long Amount { get; set; }
    public int? Rate { get; set; }
    public bool IncludedInPrices { get; set; }
    public AdjustmentSplit Split { get; set; }
    public ScopeDoc? Scope { get; set; }
    public AdjustmentSource Source { get; set; }
}

public sealed record ScopeDoc(List<string>? Units = null, List<string>? People = null);

public sealed record PayerDoc(string PersonId, long Amount);

public sealed class PaymentDoc
{
    public required string PersonId { get; set; }
    public PaymentStatus Status { get; set; }
}

public sealed record ActivityDoc(
    string Id,
    DateTimeOffset At,
    ActivityKind Kind,
    string Text,
    string? PersonId = null,
    string? UnitId = null);
