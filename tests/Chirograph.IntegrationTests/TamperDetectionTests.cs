using System.Net;
using Chirograph.IntegrationTests.Support;

namespace Chirograph.IntegrationTests;

/// <summary>
/// The core promise: the public verification page accepts the exact issued PDF and rejects a copy in which even a
/// single byte has changed.
/// </summary>
public sealed class TamperDetectionTests(ChirographWebFactory factory) : IClassFixture<ChirographWebFactory>
{
    private async Task<HtmlPage> CheckFileAsync(string verificationId, byte[] pdf)
    {
        using var verifier = factory.CreateBrowser();
        var page = await verifier.GetAsync($"/v/{verificationId}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        return await verifier.SubmitAsync(page, "#check-form", files: new Dictionary<string, (string, byte[])>
        {
            ["Upload"] = ("letter.pdf", pdf),
        });
    }

    [Fact]
    public async Task The_exact_issued_PDF_passes()
    {
        var (_, issued) = await factory.SeedIssuedDocumentAsync();

        var result = await CheckFileAsync(issued.VerificationId.Value, issued.StampedPdf);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("authentic", result.AttributeOf("#verdict", "data-verdict"));
        Assert.Contains("Genuine and unaltered", result.TextOf("#verdict"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("first byte")]
    [InlineData("middle byte")]
    [InlineData("last byte")]
    [InlineData("random byte 1")]
    [InlineData("random byte 2")]
    public async Task A_single_changed_byte_fails(string position)
    {
        var (_, issued) = await factory.SeedIssuedDocumentAsync();
        var tampered = issued.StampedPdf.ToArray();
        var index = position switch
        {
            "first byte" => 0,
            "middle byte" => tampered.Length / 2,
            "last byte" => tampered.Length - 1,
            _ => Random.Shared.Next(tampered.Length),
        };
        tampered[index] ^= 0x01; // flip one bit of one byte

        var result = await CheckFileAsync(issued.VerificationId.Value, tampered);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("file-mismatch", result.AttributeOf("#verdict", "data-verdict"));
        Assert.Contains("does not match the issued document", result.TextOf("#verdict"), StringComparison.Ordinal);
        Assert.NotEqual(result.TextOf("#issued-fingerprint"), result.TextOf("#presented-fingerprint"));
    }

    [Fact]
    public async Task A_convincing_edit_of_the_visible_text_fails()
    {
        var (_, issued) = await factory.SeedIssuedDocumentAsync();
        var forged = System.Text.Encoding.Latin1.GetBytes(
            System.Text.Encoding.Latin1.GetString(issued.StampedPdf).Replace("Associate", "Principal", StringComparison.Ordinal));
        Assert.NotEqual(issued.StampedPdf, forged);

        var result = await CheckFileAsync(issued.VerificationId.Value, forged);

        Assert.Equal("file-mismatch", result.AttributeOf("#verdict", "data-verdict"));
    }
}
