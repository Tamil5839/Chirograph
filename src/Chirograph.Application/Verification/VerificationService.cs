using Chirograph.Application.Abstractions;
using Chirograph.Application.Common;
using Chirograph.Domain.Common;
using Chirograph.Domain.Documents;
using Chirograph.Domain.Organizations;
using Chirograph.Domain.Verification;
using Microsoft.EntityFrameworkCore;

namespace Chirograph.Application.Verification;

/// <summary>
/// What anyone holding the link or QR code is shown. By construction it carries neither the employee's email address
/// nor the revocation reason.
/// </summary>
public sealed record PublicDocumentView(
    VerificationId VerificationId,
    string IssuerName,
    DomainName IssuerDomain,
    DomainVerificationMethod? IssuerVerificationMethod,
    DateTimeOffset? IssuerVerifiedAt,
    DocumentType Type,
    string EmployeeName,
    string Designation,
    DateOnly EmploymentStart,
    DateOnly EmploymentEnd,
    DateTimeOffset IssuedAt,
    DocumentFingerprint Fingerprint,
    DocumentStatus Status,
    DateTimeOffset? RevokedAt);

/// <param name="PresentedFingerprint">SHA-256 of the file the verifier uploaded, if any.</param>
public sealed record VerificationResult(
    VerificationOutcome Outcome,
    PublicDocumentView? Document,
    DocumentFingerprint? PresentedFingerprint);

/// <summary>Public verification by ID, optionally with the exact file. Every check of a real document is logged.</summary>
public sealed class VerificationService(IAppDbContext db, TimeProvider time)
{
    public Task<VerificationResult> ViewAsync(string? verificationId, CancellationToken cancellationToken = default) =>
        VerifyAsync(verificationId, file: null, declaredVerifier: null, cancellationToken);

    /// <summary>
    /// Compares the uploaded file's SHA-256 with the fingerprint recorded at issue. The file is hashed as a stream and
    /// never parsed or stored.
    /// </summary>
    public Task<VerificationResult> CheckFileAsync(
        string? verificationId,
        Stream file,
        string? declaredVerifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        return VerifyAsync(verificationId, file, declaredVerifier, cancellationToken);
    }

    private async Task<VerificationResult> VerifyAsync(
        string? verificationId,
        Stream? file,
        string? declaredVerifier,
        CancellationToken cancellationToken)
    {
        if (!VerificationId.TryParse(verificationId, out var id))
            return NotFound();

        var match = await (
                from document in db.Documents
                join organization in db.Organizations on document.OrganizationId equals organization.Id
                where document.VerificationId == id
                select new { document, organization })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (match is null)
            return NotFound();

        var presented = file is null
            ? null
            : await DocumentFingerprint.ComputeAsync(file, cancellationToken).ConfigureAwait(false);
        var outcome = VerificationOutcome.Evaluate(match.document, presented);

        db.VerificationEvents.Add(VerificationEvent.Record(match.document, outcome, declaredVerifier, time.UtcNow()));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new VerificationResult(outcome, ToPublicView(match.document, match.organization), presented);
    }

    private static VerificationResult NotFound() => new(VerificationOutcome.Evaluate(null, null), null, null);

    private static PublicDocumentView ToPublicView(IssuedDocument document, Organization issuer) => new(
        document.VerificationId,
        issuer.Name,
        issuer.Domain,
        issuer.VerificationMethod,
        issuer.VerifiedAt,
        document.Type,
        document.EmployeeName,
        document.Designation,
        document.EmploymentStart,
        document.EmploymentEnd,
        document.IssuedAt,
        document.Fingerprint,
        document.Status,
        document.RevokedAt);
}
