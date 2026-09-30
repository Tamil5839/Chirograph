using Chirograph.Domain.Common;
using Chirograph.Domain.Documents;
using Chirograph.Domain.Verification;

namespace Chirograph.Application.Documents;

public sealed record VerificationEntry(
    DateTimeOffset OccurredAt,
    VerificationKind Kind,
    DocumentStatus StatusShown,
    bool? FileMatched,
    string? DeclaredVerifier)
{
    public static VerificationEntry From(VerificationEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new VerificationEntry(entry.OccurredAt, entry.Kind, entry.StatusShown, entry.FileMatched, entry.DeclaredVerifier);
    }
}

public sealed record DocumentSummary(
    Guid Id,
    VerificationId VerificationId,
    DocumentType Type,
    string EmployeeName,
    string Designation,
    DateTimeOffset IssuedAt,
    DocumentStatus Status,
    int VerificationCount,
    DateTimeOffset? LastVerifiedAt);

/// <summary>Everything the issuing organization can see about one of its documents.</summary>
public sealed record IssuerDocumentView(
    Guid Id,
    VerificationId VerificationId,
    Uri VerificationUrl,
    DocumentType Type,
    string EmployeeName,
    EmailAddress EmployeeEmail,
    string Designation,
    DateOnly EmploymentStart,
    DateOnly EmploymentEnd,
    DateTimeOffset IssuedAt,
    EmailAddress? IssuedBy,
    DocumentFingerprint Fingerprint,
    long FileSizeBytes,
    DocumentStatus Status,
    DateTimeOffset? RevokedAt,
    string? RevocationReason,
    EmailAddress? RevokedBy,
    IReadOnlyList<VerificationEntry> Verifications);

public sealed record RecentVerification(
    Guid DocumentId,
    VerificationId VerificationId,
    string EmployeeName,
    VerificationEntry Entry);

internal static class DocumentFiles
{
    public const string PdfContentType = "application/pdf";

    public static string FileNameFor(IssuedDocument document) =>
        $"{document.Type.DisplayName().ToLowerInvariant().Replace(' ', '-')}-{document.VerificationId.Value}.pdf";
}
