using Chirograph.Application.Abstractions;
using Chirograph.Domain.Documents;
using Chirograph.Infrastructure.Pdf;
using Chirograph.UnitTests.Support;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using UglyToad.PdfPig.Actions;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Chirograph.UnitTests.Infrastructure;

public class PdfSharpStamperTests
{
    private const string Id = "7K3M9QXAB2C4DEFGH8JKMNP2Q";
    private const string Url = "https://verify.chirograph.test/v/" + Id;

    private static readonly StampContent Content = new(
        VerificationId.Parse(Id),
        new Uri(Url),
        new Uri("https://verify.chirograph.test/v"));

    private readonly PdfSharpStamper _stamper = new();

    /// <summary>Page geometries a real letter might use, including ones that trip up naive stamping.</summary>
    public static TheoryData<string> Geometries => new(PageFactories.Keys);

    private static readonly Dictionary<string, Func<byte[]>> PageFactories = new()
    {
        ["A4 portrait"] = () => TestPdfs.Create(),
        ["US Letter"] = () => TestPdfs.Create(size: PageSize.Letter),
        ["A4 landscape"] = () => TestPdfs.Create(orientation: PageOrientation.Landscape),
        ["rotated 90°"] = () => TestPdfs.Create(rotate: 90),
        ["rotated 180°"] = () => TestPdfs.Create(rotate: 180),
        ["rotated 270°"] = () => TestPdfs.Create(rotate: 270),
        ["crop box inset"] = () => TestPdfs.Create(cropBox: new PdfRectangle(new XPoint(40, 70), new XPoint(540, 790))),
        ["sample letter"] = () => SampleLetterGenerator.Generate(),
    };

    [Fact]
    public void Stamping_keeps_every_page()
    {
        var stamped = _stamper.Stamp(TestPdfs.Create(pages: 3), Content);

        using var pdf = PdfPigDocument.Open(stamped);
        Assert.Equal(3, pdf.NumberOfPages);
    }

    [Fact]
    public void Every_page_shows_the_verification_id_and_where_to_verify()
    {
        var stamped = _stamper.Stamp(TestPdfs.Create(pages: 3), Content);

        using var pdf = PdfPigDocument.Open(stamped);
        Assert.All(pdf.GetPages(), page =>
        {
            Assert.Contains("7K3M9-QXAB2-C4DEF-GH8JK-MNP2Q", page.Text, StringComparison.Ordinal);
            Assert.Contains("Verify at verify.chirograph.test/v", page.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_original_content_is_preserved()
    {
        var stamped = _stamper.Stamp(TestPdfs.Create(pages: 2), Content);

        using var pdf = PdfPigDocument.Open(stamped);
        Assert.Contains("Original letter text on page 1", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Contains("Original letter text on page 2", pdf.GetPage(2).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_realistic_letter_keeps_its_wording()
    {
        var stamped = _stamper.Stamp(SampleLetterGenerator.Generate(), Content);

        using var pdf = PdfPigDocument.Open(stamped);
        var words = string.Join(' ', pdf.GetPage(1).GetWords().Select(word => word.Text));
        Assert.Contains(
            "This is to certify that Anita Desai was employed with Acme Technologies Pvt. Ltd. as Associate Engineer " +
            "from 01 June 2019 to 31 March 2024.",
            words,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_input_is_left_untouched_and_the_output_differs_from_it()
    {
        var original = SampleLetterGenerator.Generate();
        var copy = original.ToArray();

        var stamped = _stamper.Stamp(original, Content);

        Assert.Equal(copy, original);
        Assert.NotEqual(DocumentFingerprint.Compute(original), DocumentFingerprint.Compute(stamped));
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(stamped, 0, 5), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Geometries))]
    public void The_QR_code_is_scannable_and_opens_the_verification_link(string geometry)
    {
        var stamped = _stamper.Stamp(PageFactories[geometry](), Content);

        var code = Assert.Single(PdfRendering.ReadQrCodes(stamped));
        Assert.Equal(Url, code.Text);
    }

    [Theory]
    [MemberData(nameof(Geometries))]
    public void The_stamp_sits_in_the_bottom_right_corner_as_the_page_is_displayed(string geometry)
    {
        var stamped = _stamper.Stamp(PageFactories[geometry](), Content);

        var code = Assert.Single(PdfRendering.ReadQrCodes(stamped));
        Assert.InRange(code.CenterX, 0.75, 1.0);
        Assert.InRange(code.CenterY, 0.85, 1.0);
    }

    [Fact]
    public void Every_page_of_a_multi_page_letter_carries_a_scannable_code()
    {
        var stamped = _stamper.Stamp(TestPdfs.Create(pages: 3), Content);

        Assert.All(PdfRendering.ReadQrCodes(stamped), code => Assert.Equal(Url, code.Text));
    }

    [Fact]
    public void The_stamp_links_to_the_verification_page()
    {
        var stamped = _stamper.Stamp(TestPdfs.Create(pages: 2), Content);

        using var pdf = PdfPigDocument.Open(stamped);
        Assert.All(pdf.GetPages(), page =>
        {
            var link = Assert.Single(page.GetAnnotations());
            Assert.Equal(Url, Assert.IsType<UriAction>(link.Action).Uri);
        });
    }

    [Fact]
    public void Text_of_the_stamp_lies_within_the_visible_page()
    {
        var stamped = _stamper.Stamp(TestPdfs.Create(cropBox: new PdfRectangle(new XPoint(40, 70), new XPoint(540, 790))), Content);

        using var pdf = PdfPigDocument.Open(stamped);
        var page = pdf.GetPage(1);
        var idWord = Assert.Single(page.GetWords(), word => word.Text.Contains("7K3M9", StringComparison.Ordinal));
        Assert.InRange(idWord.BoundingBox.Left, 0, page.Width);
        Assert.InRange(idWord.BoundingBox.Right, 0, page.Width);
        Assert.InRange(idWord.BoundingBox.Bottom, 0, page.Height * 0.15);
    }

    [Fact]
    public void Files_that_are_not_PDFs_are_rejected()
    {
        var error = Assert.Throws<PdfStampingException>(
            () => _stamper.Stamp("This is a Word document, honest."u8.ToArray(), Content));

        Assert.Contains("could not be read as a PDF", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Truncated_PDFs_are_rejected()
    {
        var pdf = TestPdfs.Create();

        Assert.Throws<PdfStampingException>(() => _stamper.Stamp(pdf[..(pdf.Length / 2)], Content));
    }

    [Theory]
    [InlineData("secret", "owner-secret")]
    [InlineData("", "owner-secret")]
    public void Password_protected_or_edit_restricted_PDFs_are_rejected(string userPassword, string ownerPassword)
    {
        var error = Assert.Throws<PdfStampingException>(
            () => _stamper.Stamp(TestPdfs.Encrypted(userPassword, ownerPassword), Content));

        Assert.Contains("password-protected", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Digitally_signed_PDFs_are_rejected_rather_than_having_their_signature_broken()
    {
        var error = Assert.Throws<PdfStampingException>(() => _stamper.Stamp(TestPdfs.DigitallySigned(), Content));

        Assert.Contains("digitally signed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Overly_long_documents_are_rejected()
    {
        var error = Assert.Throws<PdfStampingException>(
            () => _stamper.Stamp(TestPdfs.Create(pages: PdfSharpStamper.MaxPages + 1), Content));

        Assert.Contains("pages", error.Message, StringComparison.Ordinal);
    }
}
