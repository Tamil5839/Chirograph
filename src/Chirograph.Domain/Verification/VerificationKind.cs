namespace Chirograph.Domain.Verification;

public enum VerificationKind
{
    /// <summary>Someone opened the verification page (via the QR code, the link or by typing the ID).</summary>
    Viewed,

    /// <summary>Someone uploaded a file to compare against the issued document.</summary>
    FileChecked,
}
