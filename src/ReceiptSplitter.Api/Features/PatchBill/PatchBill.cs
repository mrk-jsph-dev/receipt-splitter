using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Infrastructure.Security;
using ReceiptSplitter.Api.Shared;
using ReceiptSplitter.Core.Shared;

namespace ReceiptSplitter.Api.Features.PatchBill;

/// <summary>
/// Anyone with the link, for their own part only. Every id in an op is looked up in this bill
/// only, so an id from another bill is refused and nothing is written. Who may send each op is
/// decided in <see cref="Authorize"/>; the organizer key may send all of them.
/// </summary>
public static class PatchBill
{
    public const long MaxWeight = 100;

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/bills/{id}", Handle).RequireRateLimiting(RateLimitPolicies.Write);

    private static Task<IResult> Handle(string id, PatchOp op, HttpContext http, BillAccess access, TimeProvider clock) =>
        access.UpdateAsync(http, id, op.GetType().Name, bill =>
        {
            if (Authorize(http, bill, op) is { } denied)
                return denied;
            var error = Apply(bill, op, clock.GetUtcNow(), access.Limits);
            if (error is null && op is JoinOp or PickOp)
                Hold(http, op is PickOp pick ? bill.FindPerson(pick.PersonId)! : bill.People[^1]);
            return error;
        });

    /// <summary>
    /// The organizer key may do anything. Others may join, pick a free name, and change only the
    /// person whose token they hold. Unknown people fall through to Apply's own 400.
    /// </summary>
    private static OpError? Authorize(HttpContext http, BillDocument bill, PatchOp op)
    {
        if (BillAccess.IsOrganizer(http, bill))
            return null;
        string? personId = op switch
        {
            JoinOp or PickOp => null,
            AddPersonOp or RemovePersonOp => "",
            RenameOp o => o.PersonId,
            ClaimOp o => o.PersonId,
            UnclaimOp o => o.PersonId,
            SetWeightOp o => o.PersonId,
            MarkPaidOp o => o.PersonId,
            UnmarkPaidOp o => o.PersonId,
            SetCardOp o => o.PersonId,
            ReleaseOp o => o.PersonId,
            _ => null, // Not a known op: Apply answers 400.
        };
        if (personId is null)
            return null;
        if (personId == "")
            return OpError.Forbidden("organizer-only");
        var person = bill.FindPerson(personId);
        return person is null || BillAccess.HoldsPerson(http, person) ? null : OpError.Forbidden("not-yours");
    }

    // The token goes back once, in a header; only its hash is kept. On a storage retry the op runs
    // again and a fresh token replaces this one, so the header always matches what was saved.
    private static void Hold(HttpContext http, PersonDoc person)
    {
        var (token, hash) = PersonToken.New();
        person.HoldHash = hash;
        http.Response.Headers[PersonToken.Header] = token;
    }

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
        UnmarkPaidOp unmarkPaid => UnmarkPaid(bill, unmarkPaid, now),
        SetCardOp setCard => SetCard(bill, setCard, now),
        PickOp pick => Pick(bill, pick, now),
        ReleaseOp release => Release(bill, release, now),
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

    private static OpError? Pick(BillDocument bill, PickOp op, DateTimeOffset now)
    {
        var person = bill.FindPerson(op.PersonId);
        if (person is null)
            return OpError.Invalid("unknown-person");
        if (person.HoldHash is not null)
            return OpError.Conflict("name-held");

        bill.Log(now, ActivityKind.Pick, $"{person.Name} opened the bill on a phone", person.Id);
        return null;
    }

    private static OpError? Release(BillDocument bill, ReleaseOp op, DateTimeOffset now)
    {
        var person = bill.FindPerson(op.PersonId);
        if (person is null)
            return OpError.Invalid("unknown-person");
        if (person.HoldHash is null)
            return null;

        person.HoldHash = null;
        bill.Log(now, ActivityKind.Release, $"{person.Name} can be picked on another phone", person.Id);
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

    private static OpError? UnmarkPaid(BillDocument bill, UnmarkPaidOp op, DateTimeOffset now)
    {
        var person = bill.FindPerson(op.PersonId);
        if (person is null)
            return OpError.Invalid("unknown-person");
        var payment = bill.Payments.Find(p => p.PersonId == person.Id);
        if (payment?.Status != op.From)
            return OpError.Conflict("changed");
        if (payment is null)
            return OpError.Invalid("not-paid");
        if (payment.Status == PaymentStatus.Confirmed)
            return OpError.Invalid("already-confirmed");

        bill.Payments.Remove(payment);
        bill.Log(now, ActivityKind.UnmarkPaid, $"{person.Name} hasn't paid yet after all", person.Id);
        return null;
    }

    private static OpError? SetCard(BillDocument bill, SetCardOp op, DateTimeOffset now)
    {
        var person = bill.FindPerson(op.PersonId);
        if (person is null)
            return OpError.Invalid("unknown-person");
        if (!Enum.IsDefined(op.Card))
            return OpError.Invalid("card");
        if (person.Card != op.From)
            return OpError.Conflict("changed");

        person.Card = op.Card;
        var text = op.Card switch
        {
            Card.Senior => $"{person.Name} has a senior card",
            Card.Pwd => $"{person.Name} has a PWD card",
            _ => $"{person.Name} has no discount card",
        };
        bill.Log(now, ActivityKind.SetCard, text, person.Id);
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
