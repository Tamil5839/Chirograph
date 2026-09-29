namespace Chirograph.Domain.Verification;

public enum VerificationVerdict
{
    /// <summary>No document has this verification ID.</summary>
    NotFound,

    /// <summary>The issuer's record exists and is valid; no file has been checked against it yet.</summary>
    RecordValid,

    /// <summary>The record is valid and the presented file is exactly the issued file.</summary>
    Authentic,

    /// <summary>The record is valid but the presented file is not the issued file.</summary>
    FileMismatch,

    /// <summary>The issuer has revoked the document.</summary>
    Revoked,
}
