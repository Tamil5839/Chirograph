using Chirograph.Application.Abstractions;
using Chirograph.Application.Access;
using Chirograph.Application.Common;
using Chirograph.Application.Documents;
using Chirograph.Domain.Access;
using Chirograph.Domain.Common;
using Chirograph.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace Chirograph.Application.Employees;

public sealed record EmployeeDocumentSummary(
    Guid Id,
    string IssuerName,
    DomainName IssuerDomain,
    DocumentType Type,
    string Designation,
    DateTimeOffset IssuedAt,
    DocumentStatus Status,
    int VerificationCount);

public sealed record EmployeeDocumentView(
    Guid Id,
    VerificationId VerificationId,
    Uri VerificationUrl,
    string IssuerName,
    DomainName IssuerDomain,
    DocumentType Type,
    string EmployeeName,
    string Designation,
    DateOnly EmploymentStart,
    DateOnly EmploymentEnd,
    DateTimeOffset IssuedAt,
    DocumentFingerprint Fingerprint,
    DocumentStatus Status,
    DateTimeOffset? RevokedAt,
    string? RevocationReason,
    IReadOnlyList<VerificationEntry> Verifications);

/// <summary>
/// Lets employees see every document issued to their email address, from any organization, and exactly when each
/// was verified: the transparency the Digital Personal Data Protection Act expects towards the data principal.
/// </summary>
public sealed class EmployeePortalService(
    IAppDbContext db,
    MagicLinkService magicLinks,
    LinkBuilder links,
    IEmailSender email,
    IFileStore files)
{
    /// <summary>Emails a sign-in link if documents exist for the address. The caller's response never reveals which.</summary>
    public async Task RequestSignInLinkAsync(string? emailAddress, CancellationToken cancellationToken = default)
    {
        if (!EmailAddress.TryParse(emailAddress, out var address, out _))
            return;
        var hasDocuments = await db.Documents.AnyAsync(d => d.EmployeeEmail == address, cancellationToken).ConfigureAwait(false);
        if (!hasDocuments)
            return;

        var rawToken = magicLinks.Issue(MagicLinkPurpose.EmployeeSignIn, address, null, null, LinkLifetimes.EmployeeSignIn);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await email.SendAsync(EmailTemplates.EmployeeSignIn(address, links.MagicLink(rawToken)), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<EmployeeSession>> CompleteSignInAsync(string? rawToken, CancellationToken cancellationToken = default)
    {
        var consumed = await magicLinks.ConsumeAsync(rawToken, [MagicLinkPurpose.EmployeeSignIn], cancellationToken).ConfigureAwait(false);
        if (!consumed.Succeeded)
            return consumed.Error;
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Errors.LinkUsed;
        }
        return new EmployeeSession(consumed.Value.Email);
    }

    public async Task<IReadOnlyList<EmployeeDocumentSummary>> ListDocumentsAsync(EmployeeSession employee, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);
        var rows = await (
                from document in db.Documents
                join organization in db.Organizations on document.OrganizationId equals organization.Id
                where document.EmployeeEmail == employee.Email
                orderby document.IssuedAt descending
                select new
                {
                    document,
                    organization.Name,
                    organization.Domain,
                    Count = db.VerificationEvents.Count(e => e.DocumentId == document.Id),
                })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .Select(r => new EmployeeDocumentSummary(
                r.document.Id, r.Name, r.Domain, r.document.Type, r.document.Designation, r.document.IssuedAt, r.document.Status, r.Count))
            .ToList();
    }

    public async Task<Result<EmployeeDocumentView>> GetDocumentAsync(EmployeeSession employee, Guid documentId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);
        var match = await (
                from document in db.Documents
                join organization in db.Organizations on document.OrganizationId equals organization.Id
                where document.Id == documentId && document.EmployeeEmail == employee.Email
                select new { document, organization })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (match is null)
            return Errors.NotFound;

        var verifications = await db.VerificationEvents
            .Where(e => e.DocumentId == documentId)
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var d = match.document;
        return new EmployeeDocumentView(
            d.Id, d.VerificationId, links.Verification(d.VerificationId), match.organization.Name, match.organization.Domain,
            d.Type, d.EmployeeName, d.Designation, d.EmploymentStart, d.EmploymentEnd, d.IssuedAt, d.Fingerprint,
            d.Status, d.RevokedAt, d.RevocationReason, verifications.Select(VerificationEntry.From).ToList());
    }

    public async Task<Result<StoredFile>> OpenFileAsync(EmployeeSession employee, Guid documentId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);
        var document = await db.Documents
            .SingleOrDefaultAsync(d => d.Id == documentId && d.EmployeeEmail == employee.Email, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
            return Errors.NotFound;
        var stream = await files.OpenReadAsync(document.FileKey, cancellationToken).ConfigureAwait(false);
        return stream is null ? Errors.NotFound : new StoredFile(stream, DocumentFiles.FileNameFor(document));
    }
}
