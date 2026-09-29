using Chirograph.Application.Abstractions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QRCoder;

namespace Chirograph.Infrastructure.Pdf;

/// <summary>
/// Draws the verification stamp in the bottom-right corner of every page, on top of the existing content, which is
/// otherwise left as it was. The stamp is built once as a form XObject and placed on each page, upright as the page
/// is displayed, whatever its size, crop box or rotation.
/// </summary>
public sealed class PdfSharpStamper : IPdfStamper
{
    public const int MaxPages = 50;

    private const double MarginRight = 18;
    private const double MarginBottom = 12;

    public byte[] Stamp(byte[] pdf, StampContent content)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(content);
        PdfFonts.EnsureRegistered();

        using var document = Open(pdf);
        if (document.PageCount == 0)
            throw new PdfStampingException("This PDF has no pages.");
        if (document.PageCount > MaxPages)
            throw new PdfStampingException($"Documents longer than {MaxPages} pages are not supported.");
        if (IsDigitallySigned(document))
        {
            throw new PdfStampingException(
                "This PDF is digitally signed, and adding the verification stamp would invalidate the signature. " +
                "Upload the unsigned version.");
        }

        // Leave the issuer's content streams encoded exactly as they were.
        document.Options.CompressContentStreams = false;

        var stamp = StampGraphic.Create(document, content);
        foreach (var page in document.Pages)
            PlaceStamp(page, stamp, content.VerificationUrl);

        using var output = new MemoryStream(pdf.Length + 32 * 1024);
        document.Save(output);
        return output.ToArray();
    }

    private static PdfDocument Open(byte[] pdf)
    {
        const string protectedMessage =
            "This PDF is password-protected or restricts editing. Upload an unprotected copy " +
            "(for example, export or print it to PDF again without a password).";
        var passwordRequested = false;
        PdfDocument? document;
        try
        {
            // PDFsharp asks for a password for encrypted files (even owner-password-only ones, since stamping
            // modifies the document); declining makes Open return null or throw.
            document = PdfReader.Open(
                new MemoryStream(pdf, writable: false),
                PdfDocumentOpenMode.Modify,
                args =>
                {
                    passwordRequested = true;
                    args.Abort = true;
                });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw passwordRequested
                ? new PdfStampingException(protectedMessage, ex)
                : new PdfStampingException(
                    "This file could not be read as a PDF. Check that it is a complete, valid PDF document.",
                    ex);
        }

        if (document is null || passwordRequested)
        {
            document?.Dispose();
            throw new PdfStampingException(protectedMessage);
        }
        return document;
    }

    private static bool IsDigitallySigned(PdfDocument document)
    {
        var acroForm = document.Internals.Catalog.Elements.GetDictionary("/AcroForm");
        const int signaturesExist = 1;
        return acroForm is not null && (acroForm.Elements.GetInteger("/SigFlags") & signaturesExist) != 0;
    }

    private static void PlaceStamp(PdfPage page, StampGraphic stamp, Uri verificationUrl)
    {
        var view = PageView.Of(page);

        // Shrink the stamp on pages too small to hold it at full size.
        var scale = Math.Min(1, Math.Min(
            (view.Width - 2 * MarginRight) / stamp.Width,
            (view.Height - 2 * MarginBottom) / stamp.Height));
        scale = Math.Max(scale, 0.1);
        var left = view.Width - MarginRight - stamp.Width * scale;
        var top = MarginBottom + stamp.Height * scale;

        // Stamp coordinates (origin top-left, y down, as displayed) → PDF user space.
        XPoint ToUserSpace(double x, double y) => view.ToUserSpace(left + x * scale, top - y * scale);

        using (var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
        {
            var toWorld = UserSpaceToWorld(gfx);
            var origin = toWorld.Transform(ToUserSpace(0, 0));
            var unitX = toWorld.Transform(ToUserSpace(1, 0)) - origin;
            var unitY = toWorld.Transform(ToUserSpace(0, 1)) - origin;

            gfx.Save();
            gfx.MultiplyTransform(new XMatrix(unitX.X, unitX.Y, unitY.X, unitY.Y, origin.X, origin.Y));
            gfx.DrawImage(stamp.Form, 0, 0, stamp.Width, stamp.Height);
            gfx.Restore();
        }

        var corners = new[]
        {
            ToUserSpace(0, 0), ToUserSpace(stamp.Width, 0),
            ToUserSpace(0, stamp.Height), ToUserSpace(stamp.Width, stamp.Height),
        };
        page.AddWebLink(
            new PdfRectangle(
                new XPoint(corners.Min(c => c.X), corners.Min(c => c.Y)),
                new XPoint(corners.Max(c => c.X), corners.Max(c => c.Y))),
            verificationUrl.AbsoluteUri);
    }

    /// <summary>
    /// PDFsharp only exposes world → page mapping; derive it from three points and invert it, which holds for
    /// whatever offset or flip PDFsharp applies to this page.
    /// </summary>
    private static XMatrix UserSpaceToWorld(XGraphics gfx)
    {
        var origin = gfx.Transformer.WorldToDefaultPage(new XPoint(0, 0));
        var unitX = gfx.Transformer.WorldToDefaultPage(new XPoint(1, 0)) - origin;
        var unitY = gfx.Transformer.WorldToDefaultPage(new XPoint(0, 1)) - origin;
        var worldToUser = new XMatrix(unitX.X, unitX.Y, unitY.X, unitY.Y, origin.X, origin.Y);
        worldToUser.Invert();
        return worldToUser;
    }

    /// <summary>A page as the reader sees it: its visible (crop) box, turned by the page's /Rotate.</summary>
    private readonly record struct PageView(double X1, double Y1, double X2, double Y2, int Rotation)
    {
        public double Width => Rotation is 90 or 270 ? Y2 - Y1 : X2 - X1;

        public double Height => Rotation is 90 or 270 ? X2 - X1 : Y2 - Y1;

        public static PageView Of(PdfPage page)
        {
            var box = page.EffectiveCropBoxReadOnly;
            var rotation = ((page.Rotate % 360) + 360) % 360;
            return new PageView(
                Math.Min(box.X1, box.X2),
                Math.Min(box.Y1, box.Y2),
                Math.Max(box.X1, box.X2),
                Math.Max(box.Y1, box.Y2),
                rotation is 90 or 180 or 270 ? rotation : 0);
        }

        /// <summary>
        /// Maps a point measured from the displayed page's bottom-left corner (x right, y up) to unrotated PDF user
        /// space. /Rotate turns the page clockwise for display, so e.g. at 90° the displayed bottom-left corner is the
        /// crop box's bottom-right corner.
        /// </summary>
        public XPoint ToUserSpace(double x, double y) => Rotation switch
        {
            90 => new XPoint(X2 - y, Y1 + x),
            180 => new XPoint(X2 - x, Y2 - y),
            270 => new XPoint(X1 + y, Y2 - x),
            _ => new XPoint(X1 + x, Y1 + y),
        };
    }

    /// <summary>The stamp artwork, drawn once per document into a reusable form XObject.</summary>
    private sealed class StampGraphic
    {
        private const double Padding = 5;
        private const double QrSize = 50;
        private const double Gap = 7;

        private static readonly XColor Ink = XColor.FromArgb(17, 24, 39);
        private static readonly XColor Muted = XColor.FromArgb(91, 102, 117);
        private static readonly XColor Border = XColor.FromArgb(156, 163, 175);

        private StampGraphic(XForm form, double width, double height)
        {
            Form = form;
            Width = width;
            Height = height;
        }

        public XForm Form { get; }

        public double Width { get; }

        public double Height { get; }

        public static StampGraphic Create(PdfDocument document, StampContent content)
        {
            var verifyAt = $"{content.VerifyPageUrl.Authority}{content.VerifyPageUrl.AbsolutePath.TrimEnd('/')}";
            var lines = new (string Text, XFont Font, XColor Color, double LineHeight)[]
            {
                ("VERIFICATION ID", PdfFonts.Create(5.5), Muted, 7.5),
                (content.VerificationId.ToDisplayString(), PdfFonts.Create(8.5, bold: true), Ink, 11),
                ($"Verify at {verifyAt}", PdfFonts.Create(6.5), Ink, 9),
                ("Scan to check this document is genuine and unaltered.", PdfFonts.Create(5.5), Muted, 7.5),
            };

            var measure = XGraphics.CreateMeasureContext(new XSize(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards);
            var textWidth = lines.Max(line => measure.MeasureString(line.Text, line.Font).Width);
            var width = Math.Ceiling(Padding + textWidth + Gap + QrSize + Padding);
            var height = Padding + QrSize + Padding;

            var form = new XForm(document, XUnit.FromPoint(width), XUnit.FromPoint(height));
            using (var gfx = XGraphics.FromForm(form))
            {
                gfx.DrawRoundedRectangle(new XPen(Border, 0.6), XBrushes.White, 0.3, 0.3, width - 0.6, height - 0.6, 6, 6);

                var y = (height - lines.Sum(line => line.LineHeight)) / 2;
                foreach (var line in lines)
                {
                    gfx.DrawString(line.Text, line.Font, new XSolidBrush(line.Color), new XPoint(Padding + 1, y), XStringFormats.TopLeft);
                    y += line.LineHeight;
                }

                DrawQrCode(gfx, content.VerificationUrl.AbsoluteUri, width - Padding - QrSize, Padding, QrSize);
            }

            return new StampGraphic(form, width, height);
        }

        /// <summary>Vector QR code: one path of dark-module runs, crisp at any zoom or print resolution.</summary>
        private static void DrawQrCode(XGraphics gfx, string payload, double x, double y, double size)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            var modules = data.ModuleMatrix; // includes the 4-module quiet zone
            var moduleSize = size / modules.Count;

            var path = new XGraphicsPath();
            for (var row = 0; row < modules.Count; row++)
            {
                var column = 0;
                while (column < modules.Count)
                {
                    if (!modules[row][column])
                    {
                        column++;
                        continue;
                    }
                    var runStart = column;
                    while (column < modules.Count && modules[row][column])
                        column++;
                    path.AddRectangle(x + runStart * moduleSize, y + row * moduleSize, (column - runStart) * moduleSize, moduleSize);
                }
            }
            gfx.DrawPath(XBrushes.Black, path);
        }
    }
}
