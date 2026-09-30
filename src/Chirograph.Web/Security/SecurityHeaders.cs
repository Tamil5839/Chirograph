using Microsoft.AspNetCore.Mvc.Filters;

namespace Chirograph.Web.Security;

/// <summary>Marks a page that search engines may index and browsers may cache (only the public landing pages).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class IndexableAttribute : Attribute;

public static class SecurityHeaders
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; object-src 'none'; " +
        "base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    /// <summary>
    /// Headers for every response. Referrer-Policy: no-referrer keeps verification IDs and magic-link tokens in URLs
    /// from leaking to other sites.
    /// </summary>
    public static IApplicationBuilder UseChirographSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), interest-cohort=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            await next(context);
        });
}

/// <summary>
/// Pages show personal data only to people holding a link, so by default none of them may be indexed or cached.
/// </summary>
public sealed class NoIndexPageFilter : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var indexable = context.HandlerInstance.GetType().IsDefined(typeof(IndexableAttribute), inherit: false);
        if (!indexable)
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.HttpContext.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        }
        await next();
    }
}
