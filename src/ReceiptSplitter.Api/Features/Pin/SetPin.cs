using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Infrastructure.Security;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Features.Pin;

/// <param name="Pin">4 to 6 digits, or null to remove the PIN.</param>
public sealed record SetPinRequest(string? Pin);

/// <summary>
/// Organizer: adds, changes or removes the bill's PIN. Only a salted hash is kept, and a new hash
/// ends every token given out for the old PIN. The activity list says it changed, never what to.
/// </summary>
public static class SetPin
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/api/bills/{id}/pin", Handle).RequireRateLimiting(RateLimitPolicies.Write);

    private static Task<IResult> Handle(string id, SetPinRequest request, HttpContext http, BillAccess access) =>
        access.OrganizerUpdateAsync(http, id, "SetPin", requireIfMatch: false, bill =>
        {
            if (request.Pin is not null && !BillPin.IsValid(request.Pin))
                return OpError.Invalid("pin");

            var text = request.Pin is null ? "The PIN was removed" : bill.PinHash is null ? "A PIN was added" : "The PIN was changed";
            if (request.Pin is null && bill.PinHash is null)
                return null;
            bill.PinHash = request.Pin is null ? null : BillPin.Hash(request.Pin);
            bill.Log(access.Clock.GetUtcNow(), ActivityKind.Pin, text);
            return null;
        });
}
