using Chirograph.Domain.Documents;

namespace Chirograph.Application.Abstractions;

/// <summary>Adds the Chirograph verification stamp (QR code, verification ID, "Verify at" link) to a PDF.</summary>
public interface IPdfStamper
{
    /// <summary>Returns a new PDF with the stamp on every page. The input is not modified.</summary>
    /// <exception cref="PdfStampingException">The file cannot be stamped (not a PDF, encrypted, signed, …).</exception>
    byte[] Stamp(byte[] pdf, StampContent content);
}

/// <param name="VerificationId">Printed on every page.</param>
/// <param name="VerificationUrl">Encoded in the QR code and used as the stamp's link, e.g. https://host/v/{id}.</param>
/// <param name="VerifyPageUrl">Where someone can type the ID, printed as "Verify at host/v".</param>
public sealed record StampContent(VerificationId VerificationId, Uri VerificationUrl, Uri VerifyPageUrl);

/// <summary>The uploaded file cannot be stamped. The message explains why and is safe to show to the issuer.</summary>
public sealed class PdfStampingException : Exception
{
    public PdfStampingException(string message)
        : base(message)
    {
    }

    public PdfStampingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
