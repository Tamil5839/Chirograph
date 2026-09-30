using System.Globalization;
using Chirograph.Application.Abstractions;
using Chirograph.Domain.Common;
using Chirograph.Domain.Documents;
using Chirograph.Domain.Organizations;

namespace Chirograph.Application.Common;

/// <summary>Every email Chirograph sends, in one place. All plain text.</summary>
internal static class EmailTemplates
{
    private const string Signature = "\n\n— Chirograph";

    public static EmailMessage ConfirmSignUp(EmailAddress to, Organization organization, Uri link) => new(
        to,
        $"Confirm your email to set up {organization.Name} on Chirograph",
        $"""
        Hello,

        You (or someone using this address) started registering "{organization.Name}" ({organization.Domain}) on Chirograph, a service for issuing employment letters that anyone can verify.

        Confirm your email address to continue:
        {link}

        This link works once and expires in 24 hours. If you did not request this, ignore this email and nothing will be set up.
        """ + Signature);

    public static EmailMessage StaffSignIn(EmailAddress to, IReadOnlyList<(string Organization, Uri Link)> links) => new(
        to,
        "Your Chirograph sign-in link",
        "Hello,\n\nUse the link below to sign in to Chirograph. Each link works once and expires in 20 minutes.\n\n"
        + string.Join("\n\n", links.Select(l => $"{l.Organization}:\n{l.Link}"))
        + "\n\nIf you did not ask to sign in, ignore this email." + Signature);

    public static EmailMessage ConfirmDomain(EmailAddress to, Organization organization, EmailAddress requestedBy, Uri link) => new(
        to,
        $"Confirm that {organization.Name} may issue documents for {organization.Domain}",
        $"""
        Hello,

        {requestedBy} asked Chirograph to register the domain {organization.Domain} for the organization "{organization.Name}".

        Once confirmed, members of this organization can issue employment letters that Chirograph will show to verifiers as coming from {organization.Domain}. Only confirm if you are responsible for this domain and recognise the request.

        Confirm the domain:
        {link}

        If you don't recognise this request, ignore this email and nothing changes. The link works once and expires in 24 hours.
        """ + Signature);

    public static EmailMessage TeamInvite(EmailAddress to, Organization organization, EmailAddress invitedBy, MemberRole role, Uri link) => new(
        to,
        $"You're invited to join {organization.Name} on Chirograph",
        $"""
        Hello,

        {invitedBy} invited you to join {organization.Name} ({organization.Domain}) on Chirograph as {(role == MemberRole.Admin ? "an admin (manage the team and domain, issue and revoke documents)" : "an issuer (issue and revoke documents)")}.

        Accept the invitation:
        {link}

        This link works once and expires in 72 hours.
        """ + Signature);

    public static EmailMessage DocumentIssued(IssuedDocument document, Organization issuer, Uri verificationUrl, Uri portalLink, Uri portal, EmailAttachment pdf) =>
        new(
            document.EmployeeEmail,
            $"Your {document.Type.DisplayName().ToLowerInvariant()} from {issuer.Name}",
            $"""
            Hello {document.EmployeeName},

            {issuer.Name} ({issuer.Domain}) has issued your {document.Type.DisplayName().ToLowerInvariant()} through Chirograph. It is attached to this email.

            Anyone you share it with can confirm it is genuine and unaltered by scanning its QR code or visiting:
            {verificationUrl}

            Keep the attached PDF exactly as it is. Any change to the file, even re-saving or re-exporting it, makes the exact-file check fail.

            See your documents and a record of every time someone verifies them:
            {portalLink}
            (This link works once and expires in 72 hours. You can always request a new one at {portal}.)

            Why you're receiving this: {issuer.Name} gave this address when issuing your letter. To let others verify the letter, Chirograph keeps only your name, this email address, your designation, your employment dates and a fingerprint of the file.
            """ + Signature)
        {
            Attachments = [pdf],
        };

    public static EmailMessage DocumentRevoked(IssuedDocument document, Organization issuer, Uri portal) => new(
        document.EmployeeEmail,
        $"Your {document.Type.DisplayName().ToLowerInvariant()} from {issuer.Name} was revoked",
        $"""
        Hello {document.EmployeeName},

        {issuer.Name} ({issuer.Domain}) revoked the {document.Type.DisplayName().ToLowerInvariant()} issued to you on {Date(document.IssuedAt)} (verification ID {document.VerificationId.ToDisplayString()}).

        Reason given: {document.RevocationReason}

        Anyone who verifies it from now on will see that it was revoked on {Date(document.RevokedAt!.Value)}. If you believe this is a mistake, please contact {issuer.Name}.

        Your documents: {portal}
        """ + Signature);

    public static EmailMessage EmployeeSignIn(EmailAddress to, Uri link) => new(
        to,
        "Your Chirograph documents sign-in link",
        $"""
        Hello,

        Use this link to see your employment documents and who has verified them:
        {link}

        It works once and expires in 20 minutes. If you did not ask for it, ignore this email.
        """ + Signature);

    private static string Date(DateTimeOffset value) => value.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
}
