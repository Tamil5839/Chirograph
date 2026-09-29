using System.Globalization;
using Chirograph.Domain.Documents;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Chirograph.Infrastructure.Pdf;

/// <summary>Fields of a generated sample letter. All names are fictional.</summary>
public sealed record SampleLetterContent
{
    public string CompanyName { get; init; } = "Acme Technologies Pvt. Ltd.";

    public string CompanyDomain { get; init; } = "acme.test";

    public string CompanyAddress { get; init; } = "No. 42, Residency Road, Bengaluru 560025";

    public DocumentType Type { get; init; } = DocumentType.ExperienceLetter;

    public string EmployeeName { get; init; } = "Anita Desai";

    public string Designation { get; init; } = "Associate Engineer";

    public DateOnly EmploymentStart { get; init; } = new(2019, 6, 1);

    public DateOnly EmploymentEnd { get; init; } = new(2024, 3, 31);

    public DateOnly LetterDate { get; init; } = new(2024, 3, 31);

    public string SignatoryName { get; init; } = "Priya Sharma";

    public string SignatoryTitle { get; init; } = "Head of People";
}

/// <summary>
/// Produces a realistic one-page employment letter for demos and tests. Page text is stored uncompressed, so a
/// same-length edit made with a plain byte replacement (e.g. "Associate" → "Principal") still yields a valid PDF,
/// which makes tamper detection easy to demonstrate.
/// </summary>
public static class SampleLetterGenerator
{
    private const double MarginX = 64;
    private static readonly XColor Brand = XColor.FromArgb(30, 58, 110);
    private static readonly XColor Muted = XColor.FromArgb(90, 98, 110);

    public static byte[] Generate(SampleLetterContent? content = null)
    {
        content ??= new SampleLetterContent();
        PdfFonts.EnsureRegistered();

        using var document = new PdfDocument();
        document.Options.CompressContentStreams = false;
        document.Options.NoCompression = true;
        document.Info.Title = $"{content.Type.DisplayName()} - {content.EmployeeName}";
        document.Info.Author = content.CompanyName;

        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var width = page.Width.Point;
            var textWidth = width - 2 * MarginX;
            var body = PdfFonts.Create(10.5);
            var y = 56.0;

            gfx.DrawString(content.CompanyName.ToUpperInvariant(), PdfFonts.Create(16, bold: true), new XSolidBrush(Brand), MarginX, y + 14);
            y += 24;
            gfx.DrawString($"{content.CompanyAddress}  |  {content.CompanyDomain}", PdfFonts.Create(8.5), new XSolidBrush(Muted), MarginX, y + 8);
            y += 18;
            gfx.DrawLine(new XPen(Brand, 1.2), MarginX, y, width - MarginX, y);
            y += 30;

            gfx.DrawString($"Date: {Format(content.LetterDate)}", body, XBrushes.Black, MarginX, y);
            y += 40;

            var title = content.Type == DocumentType.RelievingLetter ? "RELIEVING LETTER" : "EXPERIENCE LETTER";
            gfx.DrawString(title, PdfFonts.Create(13, bold: true), XBrushes.Black, new XRect(0, y - 12, width, 16), XStringFormats.TopCenter);
            y += 36;

            foreach (var paragraph in Paragraphs(content))
            {
                y = DrawParagraph(gfx, paragraph, body, MarginX, y, textWidth, 16);
                y += 10;
            }

            y += 26;
            gfx.DrawString($"For {content.CompanyName}", body, XBrushes.Black, MarginX, y);
            y += 52;
            gfx.DrawString(content.SignatoryName, PdfFonts.Create(10.5, bold: true), XBrushes.Black, MarginX, y);
            y += 15;
            gfx.DrawString(content.SignatoryTitle, body, XBrushes.Black, MarginX, y);
        }

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    private static IEnumerable<string> Paragraphs(SampleLetterContent c)
    {
        var firstName = c.EmployeeName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? c.EmployeeName;
        if (c.Type == DocumentType.RelievingLetter)
        {
            yield return "Dear " + firstName + ",";
            yield return $"This is to confirm that {c.EmployeeName}, {c.Designation}, has been relieved of all duties with " +
                         $"{c.CompanyName} with effect from the close of business on {Format(c.EmploymentEnd)}, having " +
                         $"served the company from {Format(c.EmploymentStart)}.";
            yield return $"We thank {firstName} for the contributions made during this tenure and wish {firstName} " +
                         "every success in the future.";
        }
        else
        {
            yield return "To whomsoever it may concern,";
            yield return $"This is to certify that {c.EmployeeName} was employed with {c.CompanyName} as " +
                         $"{c.Designation} from {Format(c.EmploymentStart)} to {Format(c.EmploymentEnd)}.";
            yield return $"During this period, {firstName} was a dependable member of the team and carried out all " +
                         "assigned responsibilities with diligence and integrity.";
            yield return $"We wish {firstName} every success in the future.";
        }
    }

    private static double DrawParagraph(XGraphics gfx, string text, XFont font, double x, double y, double maxWidth, double lineHeight)
    {
        var line = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && gfx.MeasureString(candidate, font).Width > maxWidth)
            {
                gfx.DrawString(line, font, XBrushes.Black, x, y);
                y += lineHeight;
                line = word;
            }
            else
            {
                line = candidate;
            }
        }
        if (line.Length > 0)
        {
            gfx.DrawString(line, font, XBrushes.Black, x, y);
            y += lineHeight;
        }
        return y;
    }

    private static string Format(DateOnly date) => date.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture);
}
