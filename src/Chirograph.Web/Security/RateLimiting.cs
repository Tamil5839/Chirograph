using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Chirograph.Web.Security;

public sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    /// <summary>Verification page views per client IP per minute.</summary>
    public int VerificationViewsPerMinute { get; set; } = 60;

    /// <summary>File checks (uploads) per client IP per ten minutes.</summary>
    public int FileChecksPerTenMinutes { get; set; } = 10;

    /// <summary>Requests that send an email (sign-up, sign-in links, invitations) per client IP per fifteen minutes.</summary>
    public int EmailRequestsPerFifteenMinutes { get; set; } = 5;

    /// <summary>Documents issued per member per hour.</summary>
    public int IssuesPerHour { get; set; } = 60;
}

public static class RateLimitPolicies
{
    public const string Verification = "verification";
    public const string EmailRequests = "email-requests";
    public const string Issuance = "issuance";

    public static IServiceCollection AddChirographRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };

            // Viewing is cheap and generous; checking files costs a hash and is tighter. Verification IDs carry 125
            // random bits, so these limits are defence in depth rather than what stops ID guessing.
            options.AddPolicy(Verification, context =>
            {
                var limits = Limits(context);
                var client = ClientKey(context);
                return HttpMethods.IsPost(context.Request.Method)
                    ? RateLimitPartition.GetFixedWindowLimiter($"file-check:{client}", _ => Window(limits.FileChecksPerTenMinutes, TimeSpan.FromMinutes(10)))
                    : RateLimitPartition.GetSlidingWindowLimiter($"view:{client}", _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = limits.VerificationViewsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                    });
            });

            options.AddPolicy(EmailRequests, context =>
                HttpMethods.IsPost(context.Request.Method)
                    ? RateLimitPartition.GetFixedWindowLimiter($"email:{ClientKey(context)}", _ => Window(Limits(context).EmailRequestsPerFifteenMinutes, TimeSpan.FromMinutes(15)))
                    : RateLimitPartition.GetNoLimiter("read"));

            options.AddPolicy(Issuance, context =>
                HttpMethods.IsPost(context.Request.Method)
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        $"issue:{context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ClientKey(context)}",
                        _ => Window(Limits(context).IssuesPerHour, TimeSpan.FromHours(1)))
                    : RateLimitPartition.GetNoLimiter("read"));
        });
        return services;
    }

    private static RateLimitOptions Limits(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

    private static FixedWindowRateLimiterOptions Window(int permits, TimeSpan window) =>
        new() { PermitLimit = permits, Window = window, QueueLimit = 0 };

    /// <summary>The client's IP address (after forwarded headers are applied, when enabled). Kept in memory only.</summary>
    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
