using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Features.Confirm;

public sealed record ConfirmRequest(string PersonId);

/// <summary>
/// Organizer: the receiver confirms a payment. "I've paid" only sets waiting; this is the other
/// side. It also works with no "I've paid" first, for someone who paid in cash.
/// </summary>
public static class ConfirmPayment
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/bills/{id}/confirm", Handle).RequireRateLimiting(RateLimitPolicies.Write);

    private static Task<IResult> Handle(string id, ConfirmRequest request, HttpContext http, BillAccess access) =>
        access.OrganizerUpdateAsync(http, id, "Confirm", requireIfMatch: false, bill =>
        {
            var person = bill.FindPerson(request.PersonId);
            if (person is null)
                return OpError.Invalid("unknown-person");
            var payment = bill.Payments.Find(p => p.PersonId == person.Id);
            if (payment?.Status == PaymentStatus.Confirmed)
                return OpError.Invalid("already-confirmed");

            if (payment is null)
                bill.Payments.Add(new PaymentDoc { PersonId = person.Id, Status = PaymentStatus.Confirmed });
            else
                payment.Status = PaymentStatus.Confirmed;
            bill.Log(access.Clock.GetUtcNow(), ActivityKind.Confirm, $"{person.Name}'s payment was confirmed", person.Id);
            return null;
        });
}
