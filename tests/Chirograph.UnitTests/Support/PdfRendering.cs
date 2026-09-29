using Docnet.Core;
using Docnet.Core.Models;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace Chirograph.UnitTests.Support;

/// <param name="Text">Decoded payload, or <c>null</c> if no QR code could be read.</param>
/// <param name="CenterX">Horizontal position of the code's centre as a fraction of the displayed page width.</param>
/// <param name="CenterY">Vertical position of the code's centre as a fraction of the displayed page height (0 = top).</param>
internal sealed record RenderedQrCode(string? Text, double CenterX, double CenterY);

/// <summary>
/// Renders pages with PDFium (as a PDF viewer would, honouring crop box and rotation) and reads the QR code with
/// ZXing, proving the printed code is actually scannable.
/// </summary>
internal static class PdfRendering
{
    private static readonly Lock Gate = new(); // PDFium is not thread-safe

    public static IReadOnlyList<RenderedQrCode> ReadQrCodes(byte[] pdf, double dpi = 150)
    {
        lock (Gate)
        {
            using var document = DocLib.Instance.GetDocReader(pdf, new PageDimensions(dpi / 72));
            var codes = new List<RenderedQrCode>();
            for (var index = 0; index < document.GetPageCount(); index++)
            {
                using var page = document.GetPageReader(index);
                var width = page.GetPageWidth();
                var height = page.GetPageHeight();
                var pixels = FlattenOntoWhite(page.GetImage());

                var source = new RGBLuminanceSource(pixels, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
                var hints = new Dictionary<DecodeHintType, object> { [DecodeHintType.TRY_HARDER] = true };
                var result = new QRCodeReader().decode(new BinaryBitmap(new HybridBinarizer(source)), hints);

                codes.Add(result is null
                    ? new RenderedQrCode(null, double.NaN, double.NaN)
                    : new RenderedQrCode(
                        result.Text,
                        result.ResultPoints.Average(p => p.X) / width,
                        result.ResultPoints.Average(p => p.Y) / height));
            }
            return codes;
        }
    }

    /// <summary>PDFium leaves unpainted areas transparent; composite them onto white paper.</summary>
    private static byte[] FlattenOntoWhite(byte[] bgra)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var alpha = bgra[i + 3];
            if (alpha == 255)
                continue;
            for (var channel = 0; channel < 3; channel++)
                bgra[i + channel] = (byte)(bgra[i + channel] * alpha / 255 + 255 - alpha);
            bgra[i + 3] = 255;
        }
        return bgra;
    }
}
