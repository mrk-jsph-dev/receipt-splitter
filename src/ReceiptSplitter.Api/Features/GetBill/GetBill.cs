using ReceiptSplitter.Api.Infrastructure.RateLimits;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Features.GetBill;

/// <summary>Anyone with the link. Answers 304 when the client already has this version, which keeps polling cheap.</summary>
public static class GetBill
{
    public static void Map(IEndpointRouteBuilder app) => app.MapGet("/api/bills/{id}", Handle).RequireRateLimiting(RateLimitPolicies.Read);

    private static async Task<IResult> Handle(string id, HttpContext http, BillAccess access)
    {
        var (stored, error) = await access.LoadAsync(http, id);
        if (error is not null)
            return error;

        var bill = stored!.Bill;
        var etag = ApiResults.ETagOf(bill);
        if (http.Request.GetTypedHeaders().IfNoneMatch.Any(tag => tag.Tag == etag || tag.Tag == "*"))
        {
            http.Response.Headers.ETag = etag;
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }
        return access.Ok(http, bill);
    }
}
