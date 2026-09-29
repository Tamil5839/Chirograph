namespace Chirograph.Domain.Verification;

public enum FileCheckResult
{
    NotAttempted,

    /// <summary>The presented file is byte-for-byte the file that was issued.</summary>
    Match,

    /// <summary>The presented file differs from the issued file in at least one byte.</summary>
    Mismatch,
}
