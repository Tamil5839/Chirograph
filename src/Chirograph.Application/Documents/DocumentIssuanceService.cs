using Chirograph.Application.Abstractions;
using Chirograph.Application.Access;
using Chirograph.Application.Common;
using Chirograph.Domain.Access;
using Chirograph.Domain.Common;
using Chirograph.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chirograph.Application.Documents;

public sealed record IssueDocumentCommand(
    DocumentType Type,
    string? EmployeeName,
    string? EmployeeEmail,
    string? Designation,
    DateOnly EmploymentStart,
    DateOnly EmploymentEnd);

public sealed record IssuedDocumentResult(
    Guid DocumentId,
    VerificationId VerificationId,
    Uri VerificationUrl,
    string FileName,
    byte[] StampedPdf,
    bool EmailedToEmployee);

/// <summary>
/// Issues a letter: stamps the uploaded PDF with its verification ID and QR code, records the SHA-256 of the stamped
/// file with the structured fields, stores the stamped file and emails it to the employee. The original upload is
/// never stored.
/// </summary>
public sealed partial class DocumentIssuanceService(
    IAppDbContext db,
    IPdfStamper stamper,
    IFileStore files,
    MagicLinkService magicLinks,
    LinkBuilder links,
    IEmailSender email,
    IOptions<ChirographOptions> options,
    TimeProvider time,
    ILogger<DocumentIssuanceService> logger)
{
    public async Task<Result<IssuedDocumentResult>> IssueAsync(
        StaffActor actor,
        IssueDocumentCommand command,
        Stream pdf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var context = await StaffContext.LoadAsync(db, actor, cancellationToken).ConfigureAwait(false);
        if (!context.Succeeded)
            return context.Error;
        var (member, organization) = context.Value;

        // Validate everything cheap before touching the file.
        DocumentDetails details;
        try
        {
            organization.EnsureCanIssueDocuments();
            if (!EmailAddress.TryParse(command.EmployeeEmail, out var employeeEmail, out var emailError))
                return Errors.Validation(emailError);
            details = DocumentDetails.Create(
                command.Type, command.EmployeeName, employeeEmail, command.Designation, command.EmploymentStart, command.EmploymentEnd);
        }
        catch (DomainException ex)
        {
            return Error.From(ex);
        }

        var upload = await Uploads.ReadPdfAsync(pdf, options.Value.MaxUploadBytes, cancellationToken).ConfigureAwait(false);
        if (!upload.Succeeded)
            return upload.Error;

        var verificationId = VerificationId.New();
        var verificationUrl = links.Verification(verificationId);
        byte[] stamped;
        try
        {
            stamped = stamper.Stamp(upload.Value, new StampContent(verificationId, verificationUrl, links.VerifyPage));
        }
        catch (PdfStampingException ex)
        {
            return new Error("document.unusable_pdf", ex.Message);
        }

        var fileKey = $"documents/{organization.Id:N}/{Guid.NewGuid():N}.pdf";
        IssuedDocument document;
        try
        {
            document = IssuedDocument.Issue(
                organization, member, details, verificationId, DocumentFingerprint.Compute(stamped), stamped.Length, fileKey, time.UtcNow());
        }
        catch (DomainException ex)
        {
            return Error.From(ex);
        }

        await files.SaveAsync(fileKey, stamped, DocumentFiles.PdfContentType, cancellationToken).ConfigureAwait(false);
        string portalToken;
        try
        {
            db.Documents.Add(document);
            portalToken = magicLinks.Issue(MagicLinkPurpose.EmployeeSignIn, document.EmployeeEmail, null, null, LinkLifetimes.EmployeeWelcome);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await files.DeleteAsync(fileKey, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        var fileName = DocumentFiles.FileNameFor(document);
        var message = EmailTemplates.DocumentIssued(
            document,
            organization,
            verificationUrl,
            links.MagicLink(portalToken),
            links.EmployeePortal,
            new EmailAttachment(fileName, DocumentFiles.PdfContentType, stamped));
        var emailed = await TrySendAsync(message, document.Id, cancellationToken).ConfigureAwait(false);

        return new IssuedDocumentResult(document.Id, verificationId, verificationUrl, fileName, stamped, emailed);
    }

    /// <summary>Emails the stamped letter to the employee again, with a fresh portal link.</summary>
    public async Task<Result> ResendToEmployeeAsync(StaffActor actor, Guid documentId, CancellationToken cancellationToken = default)
    {
        var context = await StaffContext.LoadAsync(db, actor, cancellationToken).ConfigureAwait(false);
        if (!context.Succeeded)
            return context.Error;
        var organization = context.Value.Organization;

        var document = await db.Documents
            .SingleOrDefaultAsync(d => d.Id == documentId && d.OrganizationId == organization.Id, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
            return Errors.NotFound;

        byte[] stamped;
        await using (var stored = await files.OpenReadAsync(document.FileKey, cancellationToken).ConfigureAwait(false))
        {
            if (stored is null)
                return new Error("document.file_missing", "The stored file for this document could not be found.");
            using var buffer = new MemoryStream();
            await stored.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            stamped = buffer.ToArray();
        }

        var portalToken = magicLinks.Issue(MagicLinkPurpose.EmployeeSignIn, document.EmployeeEmail, null, null, LinkLifetimes.EmployeeWelcome);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var fileName = DocumentFiles.FileNameFor(document);
        var message = EmailTemplates.DocumentIssued(
            document,
            organization,
            links.Verification(document.VerificationId),
            links.MagicLink(portalToken),
            links.EmployeePortal,
            new EmailAttachment(fileName, DocumentFiles.PdfContentType, stamped));
        return await TrySendAsync(message, document.Id, cancellationToken).ConfigureAwait(false)
            ? Result.Success()
            : new Error("email.failed", "The email could not be sent. Try again later, or download the PDF and send it yourself.");
    }

    private async Task<bool> TrySendAsync(EmailMessage message, Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            await email.SendAsync(message, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The document is issued either way; the issuer can download it or resend.
            LogEmailFailed(logger, ex, documentId);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not email document {DocumentId} to the employee")]
    private static partial void LogEmailFailed(ILogger logger, Exception exception, Guid documentId);
}
