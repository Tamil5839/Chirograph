using Chirograph.Domain.Common;
using Chirograph.Domain.Organizations;

namespace Chirograph.Domain.Documents;

/// <summary>
/// The issuer's record of a stamped letter: its structured fields, the fingerprint of the exact file handed to the
/// employee, and whether it has been revoked.
/// </summary>
public sealed class IssuedDocument
{
    public const int RevocationReasonMaxLength = 500;
    public const int FileKeyMaxLength = 200;

    private IssuedDocument()
    {
    }

    public Guid Id { get; private set; }

    public VerificationId VerificationId { get; private set; } = null!;

    public Guid OrganizationId { get; private set; }

    public Guid IssuedByMemberId { get; private set; }

    public DocumentType Type { get; private set; }

    public string EmployeeName { get; private set; } = null!;

    /// <summary>Used to deliver the letter and to let the employee sign in. Never shown on the public page.</summary>
    public EmailAddress EmployeeEmail { get; private set; } = null!;

    public string Designation { get; private set; } = null!;

    public DateOnly EmploymentStart { get; private set; }

    public DateOnly EmploymentEnd { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>SHA-256 of the final stamped PDF exactly as delivered.</summary>
    public DocumentFingerprint Fingerprint { get; private set; } = null!;

    public long FileSizeBytes { get; private set; }

    /// <summary>Opaque key of the stamped PDF in file storage.</summary>
    public string FileKey { get; private set; } = null!;

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? RevokedByMemberId { get; private set; }

    /// <summary>Shown to the issuing organization and the employee, never on the public verification page.</summary>
    public string? RevocationReason { get; private set; }

    public DocumentStatus Status => RevokedAt is null ? DocumentStatus.Valid : DocumentStatus.Revoked;

    public static IssuedDocument Issue(
        Organization issuer,
        OrganizationMember issuedBy,
        DocumentDetails details,
        VerificationId verificationId,
        DocumentFingerprint fingerprint,
        long fileSizeBytes,
        string fileKey,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(issuedBy);
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(verificationId);
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fileSizeBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileKey);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fileKey.Length, FileKeyMaxLength);

        issuer.EnsureCanIssueDocuments();
        issuedBy.EnsureCanActFor(issuer.Id);
        // One day of slack so issuers in time zones ahead of UTC (e.g. India) are not rejected around midnight.
        if (details.EmploymentStart > DateOnly.FromDateTime(now.UtcDateTime.AddDays(1)))
        {
            throw new DomainException(
                "document.invalid_dates",
                "The employment start date cannot be after the date of issue.");
        }

        return new IssuedDocument
        {
            Id = Guid.CreateVersion7(now),
            VerificationId = verificationId,
            OrganizationId = issuer.Id,
            IssuedByMemberId = issuedBy.Id,
            Type = details.Type,
            EmployeeName = details.EmployeeName,
            EmployeeEmail = details.EmployeeEmail,
            Designation = details.Designation,
            EmploymentStart = details.EmploymentStart,
            EmploymentEnd = details.EmploymentEnd,
            IssuedAt = now,
            Fingerprint = fingerprint,
            FileSizeBytes = fileSizeBytes,
            FileKey = fileKey,
        };
    }

    public void Revoke(OrganizationMember revokedBy, string? reason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(revokedBy);
        revokedBy.EnsureCanActFor(OrganizationId);
        if (Status == DocumentStatus.Revoked)
            throw new DomainException("document.already_revoked", "This document has already been revoked.");

        RevocationReason = Text.Required(reason, RevocationReasonMaxLength, "reason for revoking");
        RevokedAt = now;
        RevokedByMemberId = revokedBy.Id;
    }
}
