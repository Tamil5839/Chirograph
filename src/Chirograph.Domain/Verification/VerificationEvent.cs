using Chirograph.Domain.Common;
using Chirograph.Domain.Documents;

namespace Chirograph.Domain.Verification;

/// <summary>
/// Audit entry for one verification, visible to the issuing organization and to the employee. Deliberately holds no
/// IP address or device details: only when it happened, what was checked, what the verifier was shown, and an
/// optional self-declared verifier name.
/// </summary>
public sealed class VerificationEvent
{
    public const int DeclaredVerifierMaxLength = 200;

    private VerificationEvent()
    {
    }

    public Guid Id { get; private set; }

    public Guid DocumentId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public VerificationKind Kind { get; private set; }

    /// <summary>The document status the verifier was shown at the time.</summary>
    public DocumentStatus StatusShown { get; private set; }

    /// <summary><c>null</c> when no file was checked.</summary>
    public bool? FileMatched { get; private set; }

    /// <summary>Organization the verifier said they were checking for. Self-declared and unverified.</summary>
    public string? DeclaredVerifier { get; private set; }

    public static VerificationEvent Record(
        IssuedDocument document,
        VerificationOutcome outcome,
        string? declaredVerifier,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Verdict == VerificationVerdict.NotFound)
            throw new ArgumentException("A lookup that found no document has nothing to record against.", nameof(outcome));

        var fileChecked = outcome.FileCheck != FileCheckResult.NotAttempted;
        return new VerificationEvent
        {
            Id = Guid.CreateVersion7(now),
            DocumentId = document.Id,
            OccurredAt = now,
            Kind = fileChecked ? VerificationKind.FileChecked : VerificationKind.Viewed,
            StatusShown = document.Status,
            FileMatched = fileChecked ? outcome.FileCheck == FileCheckResult.Match : null,
            DeclaredVerifier = Text.Optional(declaredVerifier, DeclaredVerifierMaxLength),
        };
    }
}
