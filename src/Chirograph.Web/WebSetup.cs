using Chirograph.Application.Access;
using Chirograph.Application.Common;
using Chirograph.Domain.Organizations;
using Chirograph.Infrastructure.Pdf;
using Chirograph.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;

namespace Chirograph.Web;

public static class WebSetup
{
    public static IServiceCollection AddChirographWeb(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddOptions<ChirographOptions>()
            .Bind(configuration.GetSection(ChirographOptions.Section))
            .Validate(
                options => options.PublicBaseUrl is { IsAbsoluteUri: true } url && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp),
                "Chirograph:PublicBaseUrl must be the absolute http(s) address where this site is published.")
            .ValidateOnStart();
        services.AddOptions<RateLimitOptions>().Bind(configuration.GetSection(RateLimitOptions.Section));

        services.AddRazorPages(options =>
            {
                options.Conventions.AuthorizeFolder("/Org", Policies.Staff);
                options.Conventions.AuthorizePage("/Org/Team", Policies.OrganizationAdmin);
                options.Conventions.AuthorizePage("/Me/Document", Policies.Employee);
            })
            .AddMvcOptions(options => options.Filters.Add<NoIndexPageFilter>());
        services.Configure<RouteOptions>(options => options.LowercaseUrls = true);

        // Uploads are held in memory while they are hashed or stamped, never buffered to disk.
        var maxUpload = configuration.GetValue<long?>($"{ChirographOptions.Section}:{nameof(ChirographOptions.MaxUploadBytes)}")
            ?? new ChirographOptions().MaxUploadBytes;
        var formLimit = maxUpload + 1024 * 1024;
        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = formLimit;
            options.MemoryBufferThreshold = (int)Math.Min(formLimit, int.MaxValue);
        });

        services.AddAuthentication(ChirographClaims.Scheme)
            .AddCookie(ChirographClaims.Scheme, options =>
            {
                options.Cookie.Name = "__Host-chirograph";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.LoginPath = "/auth/signin";
                options.AccessDeniedPath = "/auth/denied";
                options.Events.OnRedirectToLogin = context =>
                {
                    // Employees sign in from their own page, not the HR sign-in form.
                    context.Response.Redirect(context.Request.Path.StartsWithSegments("/me", StringComparison.OrdinalIgnoreCase)
                        ? "/me"
                        : context.RedirectUri);
                    return Task.CompletedTask;
                };
                options.Events.OnValidatePrincipal = RevalidateStaffAsync;
            });
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.Staff, policy => policy.RequireClaim(ChirographClaims.Kind, ChirographClaims.StaffKind));
            options.AddPolicy(Policies.OrganizationAdmin, policy => policy
                .RequireClaim(ChirographClaims.Kind, ChirographClaims.StaffKind)
                .RequireRole(nameof(MemberRole.Admin)));
            options.AddPolicy(Policies.Employee, policy => policy.RequireClaim(ChirographClaims.Kind, ChirographClaims.EmployeeKind));
        });
        services.AddAntiforgery(options => options.Cookie.SecurePolicy = CookieSecurePolicy.Always);

        var keysPath = configuration["DataProtection:KeysPath"] ?? "App_Data/keys";
        services.AddDataProtection()
            .SetApplicationName("Chirograph")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.IsPathRooted(keysPath) ? keysPath : Path.Combine(environment.ContentRootPath, keysPath)));

        services.AddChirographRateLimiting();

        // Only enable behind a reverse proxy that is the sole way to reach the app: it makes the client IP (used for
        // rate limiting) and scheme come from X-Forwarded-* headers.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });
        return services;
    }

    public static WebApplication UseChirographWeb(this WebApplication app)
    {
        if (app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
            app.UseForwardedHeaders();
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/error");
            app.UseHsts();
        }
        app.UseStatusCodePagesWithReExecute("/error", "?code={0}");
        app.UseChirographSecurityHeaders();
        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();

        app.MapStaticAssets();
        app.MapRazorPages().WithStaticAssets();

        if (app.Environment.IsDevelopment())
        {
            // A ready-made letter for trying the issue flow locally.
            app.MapGet("/dev/sample-letter.pdf", () =>
                Results.File(SampleLetterGenerator.Generate(), "application/pdf", "sample-experience-letter.pdf"));
        }
        return app;
    }

    /// <summary>
    /// Staff cookies are checked against the database on every request: a removed member is signed out at once, and
    /// a changed role or newly verified domain is reflected in the cookie.
    /// </summary>
    private static async Task RevalidateStaffAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal is not { } user || !user.IsStaff())
            return;

        var auth = context.HttpContext.RequestServices.GetRequiredService<StaffAuthService>();
        var session = await auth.GetSessionAsync(user.ToStaffActor().MemberId, context.HttpContext.RequestAborted);
        if (session is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(ChirographClaims.Scheme);
            return;
        }
        if (user.IsStaleFor(session))
        {
            context.ReplacePrincipal(ChirographClaims.ForStaff(session));
            context.ShouldRenew = true;
        }
    }
}
