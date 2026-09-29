using Chirograph.Infrastructure.Pdf;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Chirograph.UnitTests.Support;

/// <summary>Builds small PDFs with specific page geometry or protection for stamping tests.</summary>
internal static class TestPdfs
{
    public static byte[] Create(
        int pages = 1,
        PageSize size = PageSize.A4,
        PageOrientation orientation = PageOrientation.Portrait,
        int rotate = 0,
        PdfRectangle? cropBox = null,
        Action<PdfDocument>? configure = null)
    {
        PdfFonts.EnsureRegistered();
        using var document = new PdfDocument();
        for (var number = 1; number <= pages; number++)
        {
            var page = document.AddPage();
            page.Size = size;
            page.Orientation = orientation;
            if (cropBox is not null)
                page.CropBox = cropBox;
            if (rotate != 0)
                page.Rotate = rotate;
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString($"Original letter text on page {number}", PdfFonts.Create(12), XBrushes.Black, 80, 120);
        }
        configure?.Invoke(document);
        return Save(document);
    }

    public static byte[] Encrypted(string userPassword, string ownerPassword) =>
        Create(configure: document =>
        {
            document.SecuritySettings.UserPassword = userPassword;
            document.SecuritySettings.OwnerPassword = ownerPassword;
        });

    /// <summary>A PDF whose AcroForm declares that it contains signatures (SigFlags bit 1).</summary>
    public static byte[] DigitallySigned() =>
        Create(configure: document =>
        {
            var acroForm = new PdfDictionary(document);
            acroForm.Elements.SetInteger("/SigFlags", 3);
            acroForm.Elements["/Fields"] = new PdfArray(document);
            document.Internals.AddObject(acroForm);
            document.Internals.Catalog.Elements.SetReference("/AcroForm", acroForm);
        });

    private static byte[] Save(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }
}
