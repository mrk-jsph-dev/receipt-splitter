using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Features.Confirm;

/// <summary>
/// Organizer: "Not yet". The money hasn't arrived, so the payment goes back to unpaid and the
/// person sees it on their card. Also takes back a confirm made by mistake.
/// </summary>
public static class DeclinePayment
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/bills/{id}/decline", Handle).RequireRateLimiting(RateLimitPolicies.Write);

    private static Task<IResult> Handle(string id, ConfirmRequest request, HttpContext http, BillAccess access) =>
        access.OrganizerUpdateAsync(http, id, "Decline", requireIfMatch: false, bill =>
        {
            var person = bill.FindPerson(request.PersonId);
            if (person is null)
                return OpError.Invalid("unknown-person");
            var payment = bill.Payments.Find(p => p.PersonId == person.Id);
            if (payment is null)
                return OpError.Conflict("not-paid");

            bill.Payments.Remove(payment);
            bill.Log(access.Clock.GetUtcNow(), ActivityKind.Decline, $"{person.Name}'s payment hasn't arrived yet", person.Id);
            return null;
        });
}
