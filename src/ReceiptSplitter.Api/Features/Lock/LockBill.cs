using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Features.Lock;

/// <summary>Organizer: freezes the bill once settled. After this it can only be viewed or deleted.</summary>
public static class LockBill
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/bills/{id}/lock", Handle).RequireRateLimiting(RateLimitPolicies.Write);

    private static Task<IResult> Handle(string id, HttpContext http, BillAccess access) =>
        access.OrganizerUpdateAsync(http, id, "Lock", requireIfMatch: false, bill =>
        {
            var now = access.Clock.GetUtcNow();
            bill.Status = BillStatus.Locked;
            bill.LockedAt = now;
            bill.Log(now, ActivityKind.Lock, "The bill was locked");
            return null;
        });
}
