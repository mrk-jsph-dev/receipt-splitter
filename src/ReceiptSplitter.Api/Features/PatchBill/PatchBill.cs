using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Infrastructure.Security;
using ReceiptSplitter.Api.Shared;
using ReceiptSplitter.Core.Shared;

namespace ReceiptSplitter.Api.Features.PatchBill;

/// <summary>
/// Anyone with the link. Every id in an op is looked up in this bill only, so an id from another
/// bill is refused and nothing is written.
/// </summary>
public static class PatchBill
{
    public const long MaxWeight = 100;

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/bills/{id}", Handle).RequireRateLimiting(RateLimitPolicies.Write);

    private static Task<IResult> Handle(string id, PatchOp op, HttpContext http, BillAccess access, TimeProvider clock) =>
        access.UpdateAsync(http, id, op.GetType().Name, bill => Apply(bill, op, clock.GetUtcNow(), access.Limits));

    /// <summary>Checks first, then changes the bill. Returns why it was refused, or null when applied.</summary>
    public static OpError? Apply(BillDocument bill, PatchOp op, DateTimeOffset now, BillLimits limits) => op switch
    {
        AddPersonOp add => AddPerson(bill, add.Name, ActivityKind.AddPerson, now, limits),
        JoinOp join => AddPerson(bill, join.Name, ActivityKind.Join, now, limits),
        RenameOp rename => Rename(bill, rename, now),
        RemovePersonOp remove => RemovePerson(bill, remove, now),
        ClaimOp claim => Claim(bill, claim, now),
        UnclaimOp unclaim => Unclaim(bill, unclaim, now),
        SetWeightOp setWeight => SetWeight(bill, setWeight, now),
        MarkPaidOp markPaid => MarkPaid(bill, markPaid, now),
        _ => OpError.Invalid("op"),
    };

    private static OpError? AddPerson(BillDocument bill, string rawName, ActivityKind kind, DateTimeOffset now, BillLimits limits)
    {
        var name = Text.Clean(rawName, Text.MaxPersonName);
        if (name is null)
            return OpError.Invalid("name");
        if (bill.People.Count >= limits.MaxPeople)
            return OpError.Invalid("too-many-people");
        if (bill.HasName(name))
            return OpError.Conflict("name-taken");

        var person = new PersonDoc { Id = SecureIds.NewShort(), Name = name };
        bill.People.Add(person);
        bill.Log(now, kind, kind == ActivityKind.Join ? $"{name} joined" : $"{name} was added", person.Id);
        return null;
    }

    private static OpError? Rename(BillDocument bill, RenameOp op, DateTimeOffset now)
    {
        var person = bill.FindPerson(op.PersonId);
        if (person is null)
            return OpError.Invalid("unknown-person");
        var name = Text.Clean(op.Name, Text.MaxPersonName);
        if (name is null)
            return OpError.Invalid("name");
        if (person.Name != op.From)
            return OpError.Conflict("changed");
        if (bill.HasName(name, exceptPersonId: person.Id))
            return OpError.Conflict("name-taken");

        bill.Log(now, ActivityKind.Rename, $"{person.Name} is now {name}", person.Id);
        person.Name = name;
        return null;
    }

    private static OpError? RemovePerson(BillDocument bill, RemovePersonOp op, DateTimeOffset now)
    {
        var person = bill.FindPerson(op.PersonId);
        if (person is null)
            return OpError.Invalid("unknown-person");
        if (person.Name != op.From)
            return OpError.Conflict("changed");
        if (IsReferenced(bill, person.Id))
            return OpError.Conflict("person-in-use");

        bill.People.Remove(person);
        bill.Log(now, ActivityKind.RemovePerson, $"{person.Name} was removed", person.Id);
        return null;
    }

    private static OpError? Claim(BillDocument bill, ClaimOp op, DateTimeOffset now)
    {
        if (FindForClaim(bill, op.UnitId, op.PersonId, op.From) is not { } found)
            return Refusal(bill, op.UnitId, op.PersonId, op.From);
        var (receipt, line, unit, person) = found;
        if (op.Weight is < 1 or > MaxWeight)
            return OpError.Invalid("weight");
        if (receipt.Participants.Count > 0 && !receipt.Participants.Contains(person.Id))
            return OpError.Invalid("not-participant");
        if (unit.Claims.Any(c => c.PersonId == person.Id))
            return OpError.Invalid("already-claimed");

        unit.Claims.Add(new ClaimDoc(person.Id, op.Weight));
        bill.Log(now, ActivityKind.Claim, $"{person.Name} claimed {line.Name}", person.Id, unit.Id);
        return null;
    }

    private static OpError? Unclaim(BillDocument bill, UnclaimOp op, DateTimeOffset now)
    {
        if (FindForClaim(bill, op.UnitId, op.PersonId, op.From) is not { } found)
            return Refusal(bill, op.UnitId, op.PersonId, op.From);
        var (_, line, unit, person) = found;
        var claim = unit.Claims.Find(c => c.PersonId == person.Id);
        if (claim is null)
            return OpError.Invalid("not-claimed");

        unit.Claims.Remove(claim);
        bill.Log(now, ActivityKind.Unclaim, $"{person.Name} let go of {line.Name}", person.Id, unit.Id);
        return null;
    }

    private static OpError? SetWeight(BillDocument bill, SetWeightOp op, DateTimeOffset now)
    {
        if (FindForClaim(bill, op.UnitId, op.PersonId, op.From) is not { } found)
            return Refusal(bill, op.UnitId, op.PersonId, op.From);
        var (_, line, unit, person) = found;
        if (op.Weight is < 1 or > MaxWeight)
            return OpError.Invalid("weight");
        var index = unit.Claims.FindIndex(c => c.PersonId == person.Id);
        if (index < 0)
            return OpError.Invalid("not-claimed");

        unit.Claims[index] = new ClaimDoc(person.Id, op.Weight);
        bill.Log(now, ActivityKind.SetWeight, $"{person.Name} now has {op.Weight} shares of {line.Name}", person.Id, unit.Id);
        return null;
    }

    private static OpError? MarkPaid(BillDocument bill, MarkPaidOp op, DateTimeOffset now)
    {
        var person = bill.FindPerson(op.PersonId);
        if (person is null)
            return OpError.Invalid("unknown-person");
        var payment = bill.Payments.Find(p => p.PersonId == person.Id);
        if (payment?.Status != op.From)
            return OpError.Conflict("changed");
        if (payment is not null)
            return OpError.Invalid(payment.Status == PaymentStatus.Confirmed ? "already-confirmed" : "already-waiting");

        bill.Payments.Add(new PaymentDoc { PersonId = person.Id, Status = PaymentStatus.Waiting });
        bill.Log(now, ActivityKind.MarkPaid, $"{person.Name} says they've paid", person.Id);
        return null;
    }

    private static (ReceiptDoc, LineDoc, UnitDoc, PersonDoc)? FindForClaim(BillDocument bill, string unitId, string personId, IReadOnlyList<ClaimDoc> from)
    {
        var person = bill.FindPerson(personId);
        if (person is null || bill.FindUnit(unitId) is not { } found)
            return null;
        if (found.Receipt.SplitMode != SplitMode.Items || !SameClaims(found.Unit.Claims, from))
            return null;
        return (found.Receipt, found.Line, found.Unit, person);
    }

    // Says why FindForClaim came back empty, in the order a client can act on.
    private static OpError Refusal(BillDocument bill, string unitId, string personId, IReadOnlyList<ClaimDoc> from)
    {
        if (bill.FindPerson(personId) is null)
            return OpError.Invalid("unknown-person");
        if (bill.FindUnit(unitId) is not { } found)
            return OpError.Invalid("unknown-unit");
        if (found.Receipt.SplitMode != SplitMode.Items)
            return OpError.Invalid("not-by-item");
        return OpError.Conflict("changed");
    }

    private static bool SameClaims(IReadOnlyList<ClaimDoc> current, IReadOnlyList<ClaimDoc> seen) =>
        current.Count == seen.Count &&
        current.OrderBy(c => c.PersonId, StringComparer.Ordinal)
            .SequenceEqual(seen.OrderBy(c => c.PersonId, StringComparer.Ordinal));

    private static bool IsReferenced(BillDocument bill, string personId) =>
        bill.Payments.Any(p => p.PersonId == personId) ||
        bill.Receipts.Any(r =>
            r.Participants.Contains(personId) ||
            r.Payers.Any(p => p.PersonId == personId) ||
            r.Shares?.ContainsKey(personId) == true ||
            r.Adjustments.Any(a => a.Scope?.People?.Contains(personId) == true) ||
            r.Lines.Any(l => l.Units.Any(u => u.Claims.Any(c => c.PersonId == personId))));
}
