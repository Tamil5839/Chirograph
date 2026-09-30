using System.Net;
using Chirograph.IntegrationTests.Support;
using Chirograph.UnitTests.Support;

namespace Chirograph.IntegrationTests;

public sealed class VerificationPageTests(ChirographWebFactory factory) : IClassFixture<ChirographWebFactory>
{
    [Fact]
    public async Task The_QR_code_on_the_letter_opens_its_verification_page()
    {
        var (_, issued) = await factory.SeedIssuedDocumentAsync();
        var qr = Assert.Single(PdfRendering.ReadQrCodes(issued.StampedPdf)).Text;
        Assert.StartsWith(ChirographWebFactory.PublicBaseUrl + "/v/", qr, StringComparison.Ordinal);
        using var verifier = factory.CreateBrowser();

        var page = await verifier.GetAsync(qr!);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("record-valid", page.AttributeOf("#verdict", "data-verdict"));
    }

    [Fact]
    public async Task The_page_shows_the_issuer_its_verified_domain_and_the_letter_details()
    {
        var domain = ChirographWebFactory.UniqueDomain();
        var (_, issued) = await factory.SeedIssuedDocumentAsync(domain);
        using var verifier = factory.CreateBrowser();

        var page = await verifier.GetAsync($"/v/{issued.VerificationId}");

        Assert.Equal("Acme Technologies", page.TextOf("#issuer-name"));
        Assert.Equal(domain, page.TextOf("#issuer-domain"));
        Assert.Equal("Experience letter", page.TextOf("#document-type"));
        Assert.Equal("Anita Desai", page.TextOf("#employee-name"));
        Assert.Equal("Associate Engineer", page.TextOf("#designation"));
        Assert.Equal("1 Jun 2019 – 31 Mar 2024", page.TextOf("#employment"));
        Assert.Contains("Valid", page.TextOf("#status"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_id_is_reported_as_not_found()
    {
        using var verifier = factory.CreateBrowser();

        var page = await verifier.GetAsync("/v/0000000000000000000000000");

        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Equal("not-found", page.AttributeOf("#verdict", "data-verdict"));
    }

    [Fact]
    public async Task A_hand_typed_id_is_redirected_to_its_canonical_address()
    {
        var (_, issued) = await factory.SeedIssuedDocumentAsync();
        using var verifier = factory.CreateBrowser(followRedirects: false);

        using var fromPath = await verifier.GetRawAsync($"/v/{issued.VerificationId.ToDisplayString().ToLowerInvariant()}");
        using var fromForm = await verifier.GetRawAsync($"/v?id={Uri.EscapeDataString(" " + issued.VerificationId.ToDisplayString() + " ")}");

        Assert.Equal(HttpStatusCode.Redirect, fromPath.StatusCode);
        Assert.Equal($"/v/{issued.VerificationId.Value.ToLowerInvariant()}", fromPath.Headers.Location?.OriginalString, ignoreCase: true);
        Assert.Equal(HttpStatusCode.Redirect, fromForm.StatusCode);
        Assert.Equal($"/v/{issued.VerificationId.Value}", fromForm.Headers.Location?.OriginalString, ignoreCase: true);
    }

    [Fact]
    public async Task Garbage_typed_into_the_lookup_form_is_explained()
    {
        using var verifier = factory.CreateBrowser();

        var page = await verifier.GetAsync("/v?id=hello-world");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("isn't a valid verification ID", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_revoked_letter_shows_as_revoked_with_its_date()
    {
        var (admin, issued) = await factory.SeedIssuedDocumentAsync();
        await factory.RunAsync<Chirograph.Application.Documents.IssuerDocumentsService, Chirograph.Application.Common.Result>(
            s => s.RevokeAsync(admin, issued.DocumentId, "Issued in error", TestContext.Current.CancellationToken));
        using var verifier = factory.CreateBrowser();

        var page = await verifier.GetAsync($"/v/{issued.VerificationId}");

        Assert.Equal("revoked", page.AttributeOf("#verdict", "data-verdict"));
        Assert.Matches(@"Revoked on \d{1,2} \w{3} \d{4}", page.TextOf("#verdict"));
    }
}
