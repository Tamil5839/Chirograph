using Chirograph.Domain.Documents;

namespace Chirograph.Domain.Verification;

/// <summary>
/// The result a verifier sees. Pure function of the issuer's record and, optionally, the fingerprint of a file the
/// verifier presented.
/// </summary>
public sealed record VerificationOutcome
{
    private VerificationOutcome(VerificationVerdict verdict, FileCheckResult fileCheck, DateTimeOffset? revokedAt)
    {
        Verdict = verdict;
        FileCheck = fileCheck;
        RevokedAt = revokedAt;
    }

    public VerificationVerdict Verdict { get; }

    public FileCheckResult FileCheck { get; }

    public DateTimeOffset? RevokedAt { get; }

    /// <summary>Only an unrevoked record together with a byte-identical file is a full pass.</summary>
    public bool Passed => Verdict == VerificationVerdict.Authentic;

    /// <summary>The verifier should not rely on the document.</summary>
    public bool Failed => Verdict is VerificationVerdict.NotFound or VerificationVerdict.FileMismatch or VerificationVerdict.Revoked;

    public static VerificationOutcome Evaluate(IssuedDocument? document, DocumentFingerprint? presentedFile)
    {
        if (document is null)
            return new VerificationOutcome(VerificationVerdict.NotFound, FileCheckResult.NotAttempted, null);

        var fileCheck = presentedFile switch
        {
            null => FileCheckResult.NotAttempted,
            _ when document.Fingerprint.Matches(presentedFile) => FileCheckResult.Match,
            _ => FileCheckResult.Mismatch,
        };

        if (document.Status == DocumentStatus.Revoked)
            return new VerificationOutcome(VerificationVerdict.Revoked, fileCheck, document.RevokedAt);

        var verdict = fileCheck switch
        {
            FileCheckResult.Match => VerificationVerdict.Authentic,
            FileCheckResult.Mismatch => VerificationVerdict.FileMismatch,
            _ => VerificationVerdict.RecordValid,
        };
        return new VerificationOutcome(verdict, fileCheck, null);
    }
}
