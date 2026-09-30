using System.Net;
using Chirograph.Application.Common;
using Chirograph.Application.Documents;
using Chirograph.IntegrationTests.Support;

namespace Chirograph.IntegrationTests;

public sealed class PrivacyAndSecurityTests(ChirographWebFactory factory) : IClassFixture<ChirographWebFactory>
{
    [Fact]
    public async Task Verification_pages_are_hidden_from_search_engines_and_caches_and_leak_no_referrer()
    {
        var (_, issued) = await factory.SeedIssuedDocumentAsync();
        using var verifier = factory.CreateBrowser();

        using var response = await verifier.GetRawAsync($"/v/{issued.VerificationId}");

        Assert.Contains("noindex", Header(response, "X-Robots-Tag"), StringComparison.Ordinal);
        Assert.Contains("no-store", Header(response, "Cache-Control"), StringComparison.Ordinal);
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Contains("default-src 'self'", Header(response, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", Header(response, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/privacy")]
    public async Task Only_the_landing_pages_may_be_indexed(string path)
    {
        using var visitor = factory.CreateBrowser();

        using var response = await visitor.GetRawAsync(path);

        Assert.False(response.Headers.Contains("X-Robots-Tag"));
    }

    [Fact]
    public async Task The_public_page_never_shows_the_employee_email_or_the_revocation_reason()
    {
        var (admin, issued) = await factory.SeedIssuedDocumentAsync();
        await factory.RunAsync<IssuerDocumentsService, Result>(
            s => s.RevokeAsync(admin, issued.DocumentId, "Confidential disciplinary matter", TestContext.Current.CancellationToken));
        using var verifier = factory.CreateBrowser();

        var page = await verifier.GetAsync($"/v/{issued.VerificationId}");

        Assert.Equal("revoked", page.AttributeOf("#verdict", "data-verdict"));
        Assert.DoesNotContain("anita.desai@example.org", page.Document.Source.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Confidential disciplinary matter", page.Document.Source.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Files_checked_on_the_public_page_are_never_stored()
    {
        var (_, issued) = await factory.SeedIssuedDocumentAsync();
        var storedBefore = Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories).Length;
        using var verifier = factory.CreateBrowser();

        var page = await verifier.GetAsync($"/v/{issued.VerificationId}");
        foreach (var file in new[] { issued.StampedPdf, "%PDF-1.7 something else"u8.ToArray() })
            page = await verifier.SubmitAsync(page, "#check-form", files: new Dictionary<string, (string, byte[])> { ["Upload"] = ("x.pdf", file) });

        Assert.Equal(storedBefore, Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories).Length);
    }

    [Theory]
    [InlineData("/dev/mailbox")]
    [InlineData("/dev/sample-letter.pdf")]
    public async Task Development_tools_are_unavailable_outside_development(string path)
    {
        using var visitor = factory.CreateBrowser();

        using var response = await visitor.GetRawAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Opening_an_emailed_link_does_not_use_it_up_until_the_person_continues()
    {
        await factory.SeedIssuedDocumentAsync();
        using var employee = factory.CreateBrowser();
        await employee.SubmitAsync(await employee.GetAsync("/me"), "#employee-signin-form", new Dictionary<string, string> { ["Email"] = "anita.desai@example.org" });
        var link = factory.LatestLinkTo("anita.desai@example.org", "documents sign-in link");

        await employee.GetAsync(link); // e.g. a mail scanner pre-fetching the link
        var opened = await employee.GetAsync(link);
        Assert.NotNull(opened.Document.QuerySelector("#link-form"));

        var signedIn = await employee.SubmitAsync(opened, "#link-form");
        Assert.Equal("/me", signedIn.Url.AbsolutePath);
        Assert.Contains("already been used", (await employee.GetAsync(link)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_sign_in_cookie_is_host_only_secure_and_http_only()
    {
        await factory.SeedIssuedDocumentAsync();
        using var employee = factory.CreateBrowser(followRedirects: false);
        await employee.SubmitAsync(await employee.GetAsync("/me"), "#employee-signin-form", new Dictionary<string, string> { ["Email"] = "anita.desai@example.org" });
        var link = await employee.GetAsync(factory.LatestLinkTo("anita.desai@example.org", "documents sign-in link"));

        using var response = await employee.SubmitRawAsync(link, "#link-form");

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-chirograph=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(", ", values)
            : string.Empty;
}
