using Chirograph.Application.Abstractions;
using Chirograph.Application.Common;
using Chirograph.Domain.Common;
using Chirograph.Domain.Documents;
using Chirograph.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Chirograph.Application.Documents;

/// <summary>The issuing organization's view of its documents: listing, details, audit log, download and revocation.</summary>
public sealed partial class IssuerDocumentsService(
    IAppDbContext db,
    IFileStore files,
    LinkBuilder links,
    IEmailSender email,
    TimeProvider time,
    ILogger<IssuerDocumentsService> logger)
{
    private const int MaxListed = 200;

    public async Task<Result<IReadOnlyList<DocumentSummary>>> ListAsync(StaffActor actor, string? search, CancellationToken cancellationToken = default)
    {
        var context = await StaffContext.LoadAsync(db, actor, cancellationToken).ConfigureAwait(false);
        if (!context.Succeeded)
            return context.Error;

        var query = db.Documents.Where(d => d.OrganizationId == actor.OrganizationId);
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            if (VerificationId.TryParse(term, out var id))
            {
                query = query.Where(d => d.VerificationId == id);
            }
            else
            {
                var lowered = term.ToLowerInvariant();
                query = query.Where(d => d.EmployeeName.ToLower().Contains(lowered));
            }
        }

        var rows = await query
            .OrderByDescending(d => d.IssuedAt)
            .Take(MaxListed)
            .Select(d => new
            {
                Document = d,
                Count = db.VerificationEvents.Count(e => e.DocumentId == d.Id),
                Last = db.VerificationEvents.Where(e => e.DocumentId == d.Id)
                    .OrderByDescending(e => e.OccurredAt)
                    .Select(e => (DateTimeOffset?)e.OccurredAt)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .Select(r => new DocumentSummary(
                r.Document.Id, r.Document.VerificationId, r.Document.Type, r.Document.EmployeeName, r.Document.Designation,
                r.Document.IssuedAt, r.Document.Status, r.Count, r.Last))
            .ToList();
    }

    public async Task<Result<IssuerDocumentView>> GetAsync(StaffActor actor, Guid documentId, CancellationToken cancellationToken = default)
    {
        var context = await StaffContext.LoadAsync(db, actor, cancellationToken).ConfigureAwait(false);
        if (!context.Succeeded)
            return context.Error;

        var document = await FindAsync(actor, documentId, cancellationToken).ConfigureAwait(false);
        if (document is null)
            return Errors.NotFound;

        var memberIds = new[] { document.IssuedByMemberId, document.RevokedByMemberId ?? Guid.Empty };
        var emails = await db.OrganizationMembers
            .Where(m => memberIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Email, cancellationToken).ConfigureAwait(false);
        var verifications = await db.VerificationEvents
            .Where(e => e.DocumentId == document.Id)
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new IssuerDocumentView(
            document.Id,
            document.VerificationId,
            links.Verification(document.VerificationId),
            document.Type,
            document.EmployeeName,
            document.EmployeeEmail,
            document.Designation,
            document.EmploymentStart,
            document.EmploymentEnd,
            document.IssuedAt,
            emails.GetValueOrDefault(document.IssuedByMemberId),
            document.Fingerprint,
            document.FileSizeBytes,
            document.Status,
            document.RevokedAt,
            document.RevocationReason,
            document.RevokedByMemberId is { } revokedBy ? emails.GetValueOrDefault(revokedBy) : null,
            verifications.Select(VerificationEntry.From).ToList());
    }

    public async Task<Result<IReadOnlyList<RecentVerification>>> RecentVerificationsAsync(
        StaffActor actor,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        var context = await StaffContext.LoadAsync(db, actor, cancellationToken).ConfigureAwait(false);
        if (!context.Succeeded)
            return context.Error;

        var rows = await (
                from entry in db.VerificationEvents
                join document in db.Documents on entry.DocumentId equals document.Id
                where document.OrganizationId == actor.OrganizationId
                orderby entry.OccurredAt descending
                select new { entry, document.Id, document.VerificationId, document.EmployeeName })
            .Take(take)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => new RecentVerification(r.Id, r.VerificationId, r.EmployeeName, VerificationEntry.From(r.entry))).ToList();
    }

    public async Task<Result<StoredFile>> OpenFileAsync(StaffActor actor, Guid documentId, CancellationToken cancellationToken = default)
    {
        var context = await StaffContext.LoadAsync(db, actor, cancellationToken).ConfigureAwait(false);
        if (!context.Succeeded)
            return context.Error;

        var document = await FindAsync(actor, documentId, cancellationToken).ConfigureAwait(false);
        if (document is null)
            return Errors.NotFound;
        var stream = await files.OpenReadAsync(document.FileKey, cancellationToken).ConfigureAwait(false);
        return stream is null ? Errors.NotFound : new StoredFile(stream, DocumentFiles.FileNameFor(document));
    }

    /// <summary>Revokes a document; verifiers see "Revoked" with the date, and the employee is told why.</summary>
    public async Task<Result> RevokeAsync(StaffActor actor, Guid documentId, string? reason, CancellationToken cancellationToken = default)
    {
        var context = await StaffContext.LoadAsync(db, actor, cancellationToken).ConfigureAwait(false);
        if (!context.Succeeded)
            return context.Error;
        var (member, organization) = context.Value;

        var document = await FindAsync(actor, documentId, cancellationToken).ConfigureAwait(false);
        if (document is null)
            return Errors.NotFound;

        try
        {
            document.Revoke(member, reason, time.UtcNow());
        }
        catch (DomainException ex)
        {
            return Error.From(ex);
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await NotifyEmployeeAsync(document, organization, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private Task<IssuedDocument?> FindAsync(StaffActor actor, Guid documentId, CancellationToken cancellationToken) =>
        db.Documents.SingleOrDefaultAsync(d => d.Id == documentId && d.OrganizationId == actor.OrganizationId, cancellationToken);

    private async Task NotifyEmployeeAsync(IssuedDocument document, Organization organization, CancellationToken cancellationToken)
    {
        try
        {
            await email.SendAsync(EmailTemplates.DocumentRevoked(document, organization, links.EmployeePortal), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNotificationFailed(logger, ex, document.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not tell the employee that document {DocumentId} was revoked")]
    private static partial void LogNotificationFailed(ILogger logger, Exception exception, Guid documentId);
}
