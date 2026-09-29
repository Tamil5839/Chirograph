using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace Chirograph.Infrastructure.Pdf;

/// <summary>
/// Registers the embedded Liberation Sans faces with PDFsharp. PDFsharp's font settings are process-wide, so this
/// happens exactly once; every family name resolves to Liberation Sans.
/// </summary>
internal static class PdfFonts
{
    public const string Family = "Liberation Sans";

    private static readonly Lazy<bool> Registration = new(() =>
    {
        GlobalFontSettings.FontResolver ??= new EmbeddedFontResolver();
        return true;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void EnsureRegistered() => _ = Registration.Value;

    /// <summary>
    /// WinAnsi-encoded font: text is written as plain strings, which keeps stamps and sample letters readable by
    /// any PDF text extractor. All stamp text is ASCII.
    /// </summary>
    public static XFont Create(double size, bool bold = false)
    {
        EnsureRegistered();
        return new XFont(
            Family,
            size,
            bold ? XFontStyleEx.Bold : XFontStyleEx.Regular,
            new XPdfFontOptions(PdfFontEncoding.WinAnsi));
    }

    private sealed class EmbeddedFontResolver : IFontResolver
    {
        private const string Regular = "LiberationSans-Regular";
        private const string Bold = "LiberationSans-Bold";

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
            new(bold ? Bold : Regular);

        public byte[]? GetFont(string faceName)
        {
            if (faceName is not (Regular or Bold))
                return null;

            using var stream = typeof(PdfFonts).Assembly.GetManifestResourceStream($"Chirograph.Fonts.{faceName}.ttf")
                ?? throw new InvalidOperationException($"Embedded font {faceName} is missing.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}
