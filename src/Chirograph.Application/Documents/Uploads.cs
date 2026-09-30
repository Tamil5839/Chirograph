using Chirograph.Application.Common;

namespace Chirograph.Application.Documents;

internal static class Uploads
{
    private const int PdfHeaderWindow = 1024;

    /// <summary>Reads an upload into memory, refusing anything over <paramref name="maxBytes"/> or not a PDF.</summary>
    public static async Task<Result<byte[]>> ReadPdfAsync(Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maxBytes)
                return new Error("upload.too_large", $"The file is larger than the {maxBytes / (1024 * 1024)} MB limit.");
            buffer.Write(chunk, 0, read);
        }

        var bytes = buffer.ToArray();
        return LooksLikePdf(bytes)
            ? bytes
            : new Error("upload.not_pdf", "That file is not a PDF. Upload the letter as a PDF document.");
    }

    /// <summary>The PDF specification allows the <c>%PDF-</c> header anywhere in the first 1024 bytes.</summary>
    public static bool LooksLikePdf(ReadOnlySpan<byte> bytes) =>
        bytes[..Math.Min(bytes.Length, PdfHeaderWindow)].IndexOf("%PDF-"u8) >= 0;
}
