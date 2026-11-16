using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Features.Undo;

/// <param name="ActivityId">The "Ben claimed Gyoza" entry to undo.</param>
public sealed record UndoRequest(string ActivityId);

/// <summary>Organizer: takes back a claim named by its activity entry, for mistakes or mischief.</summary>
public static class UndoClaim
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/bills/{id}/undo", Handle).RequireRateLimiting(RateLimitPolicies.Write);

    private static Task<IResult> Handle(string id, UndoRequest request, HttpContext http, BillAccess access) =>
        access.OrganizerUpdateAsync(http, id, "Undo", requireIfMatch: false, bill =>
        {
            var entry = bill.Activity.Find(a => a.Id == request.ActivityId);
            if (entry is not { Kind: ActivityKind.Claim, PersonId: { } personId, UnitId: { } unitId })
                return OpError.Invalid("not-a-claim");

            // The claim may already be gone: let go of, removed with its line, or undone before.
            var found = bill.FindUnit(unitId);
            var claim = found?.Unit.Claims.Find(c => c.PersonId == personId);
            if (claim is null)
                return OpError.Conflict("already-undone");

            found!.Value.Unit.Claims.Remove(claim);
            bill.Log(access.Clock.GetUtcNow(), ActivityKind.Undo, $"{bill.NameOf(personId)}'s claim on {found.Value.Line.Name} was undone", personId, unitId);
            return null;
        });
}
