using Microsoft.AspNetCore.HttpOverrides;
using ReceiptSplitter.Api.Infrastructure.Security;

namespace ReceiptSplitter.Api.Infrastructure.Http;

/// <summary>Headers, CORS and client IP handling from docs/SPEC.md section 9.6.</summary>
public static class HttpHardening
{
    public const string CorsPolicy = "app";
    public const int MaxBodyBytes = 256 * 1024;

    /// <summary>A scan: one file of up to 4 MB plus the small form fields around it.</summary>
    public const int MaxScanBodyBytes = 4 * 1024 * 1024 + 64 * 1024;

    public static bool IsScan(HttpRequest request) => request.Path.StartsWithSegments("/api/scans");

    public static IServiceCollection AddHttpHardening(this IServiceCollection services, IConfiguration config)
    {
        var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(cors => cors.AddPolicy(CorsPolicy, policy => policy
            .WithOrigins(origins)
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
            .WithHeaders("Content-Type", "If-None-Match", "If-Match", OrganizerKey.Header, BillPin.Header, PersonToken.Header)
            .WithExposedHeaders("ETag", "Retry-After", PersonToken.Header)));

        // Container Apps ingress appends the caller's IP to X-Forwarded-For. Only the last entry is
        // trusted, so a client can't pick its own IP to dodge the per-IP limits.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        services.ConfigureHttpJsonOptions(options => ApiJson.Configure(options.SerializerOptions));
        return services;
    }

    public static WebApplication UseHttpHardening(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.Use(async (http, next) =>
        {
            var headers = http.Response.Headers;
            headers.CacheControl = "no-store";
            headers["Referrer-Policy"] = "no-referrer";
            headers["X-Robots-Tag"] = "noindex";
            headers["X-Content-Type-Options"] = "nosniff";
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

            // Kestrel allows a scan's size; everything else keeps the small limit, chunked or not.
            var limit = IsScan(http.Request) ? MaxScanBodyBytes : MaxBodyBytes;
            if (http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
                size.MaxRequestBodySize = limit;
            if (http.Request.ContentLength > limit)
            {
                http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }
            await next(http);
        });
        app.UseCors(CorsPolicy);
        return app;
    }
}
