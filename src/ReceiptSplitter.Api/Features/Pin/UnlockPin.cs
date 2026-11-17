using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Infrastructure.Security;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Features.Pin;

public sealed record UnlockRequest(string? Pin);

/// <summary>The phone keeps this for the bill and sends it in the X-Bill-Pin header.</summary>
public sealed record Unlocked(string Token);

public sealed record PinRefusal(string Error, string? Reason = null, int? TriesLeft = null);

/// <summary>
/// A friend trades the PIN for a token. Wrong tries are counted per bill: 5 in 15 minutes, or 20
/// in a day, lock it with 429 and Retry-After. Unknown bills get the same 404 as everywhere else.
/// </summary>
public static class UnlockPin
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/bills/{id}/pin/unlock", Handle).RequireRateLimiting(RateLimitPolicies.Write);

    private static async Task<IResult> Handle(string id, UnlockRequest request, HttpContext http, BillAccess access, PinGuard guard)
    {
        var (stored, error) = await access.LoadAsync(http, id);
        if (error is not null)
            return error;
        var bill = stored!.Bill;
        if (bill.PinHash is null)
            return ApiResults.Invalid("no-pin");

        var state = await guard.CheckAsync(bill.Id, http.RequestAborted);
        if (state.LockedFor is { } wait)
        {
            http.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
            return Results.Json(new PinRefusal("rate-limited", "pin-locked", 0), statusCode: StatusCodes.Status429TooManyRequests);
        }

        if (!BillPin.Matches(request.Pin, bill.PinHash))
        {
            var left = await guard.RecordWrongAsync(bill.Id, http.RequestAborted);
            return Results.Json(new PinRefusal("pin-wrong", TriesLeft: left), statusCode: StatusCodes.Status401Unauthorized);
        }
        return Results.Ok(new Unlocked(BillPin.Token(bill.PinHash, bill.Id)));
    }
}
