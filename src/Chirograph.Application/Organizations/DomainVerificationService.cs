using Chirograph.Application.Abstractions;
using Chirograph.Application.Access;
using Chirograph.Application.Common;
using Chirograph.Domain.Access;
using Chirograph.Domain.Common;
using Chirograph.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Chirograph.Application.Organizations;

public sealed record DomainVerificationStatus(
    Guid OrganizationId,
    string OrganizationName,
    DomainName Domain,
    bool IsVerified,
    DomainVerificationMethod? Method,
    string? Evidence,
    DateTimeOffset? VerifiedAt,
    string DnsRecordName,
    string DnsRecordValue,
    IReadOnlyList<EmailAddress> AdminMailboxes);

/// <summary>
/// Proves an organization controls its domain, either by a DNS TXT record or by a link confirmed from an
/// administrative mailbox on the domain. Until then the organization cannot issue documents.
/// </summary>
public sealed class DomainVerificationService(
    IAppDbContext db,
    MagicLinkService magicLinks,
    LinkBuilder links,
    IEmailSender email,
    IDnsTxtResolver dns,
    TimeProvider time)
{
    public async Task<Result<DomainVerificationStatus>> GetStatusAsync(StaffActor actor, CancellationToken cancellationToken = default)
    {
        var context = await StaffContext.LoadAsync(db, actor, cancellationToken).ConfigureAwait(false);
        return context.Succeeded ? Status(context.Value.Organization) : context.Error;
    }

    public async Task<Result<DomainVerificationStatus>> CheckDnsAsync(StaffActor actor, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadPendingForAdminAsync(actor, cancellationToken).ConfigureAwait(false);
        if (!loaded.Succeeded)
            return loaded.Error;
        var organization = loaded.Value.Organization;
        if (organization.IsVerified)
            return Status(organization);

        foreach (var host in new[] { organization.DnsRecordName, organization.Domain.Value })
        {
            var records = await dns.GetTxtRecordsAsync(host, cancellationToken).ConfigureAwait(false);
            if (records.Any(organization.IsProvenBy))
                return await MarkVerifiedAsync(organization, DomainVerificationMethod.DnsTxtRecord, $"TXT record at {host}", cancellationToken).ConfigureAwait(false);
        }

        return new Error(
            "domain.dns_record_not_found",
            $"We couldn't find the TXT record yet. Add a TXT record at {organization.DnsRecordName} with the value shown, then check again. " +
            "New DNS records usually appear within minutes, but can take longer.");
    }

    /// <summary>Emails a confirmation link to an administrative mailbox (admin@, hostmaster@, …) on the domain.</summary>
    public async Task<Result<EmailAddress>> SendAdminMailboxLinkAsync(StaffActor actor, string? mailbox, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadPendingForAdminAsync(actor, cancellationToken).ConfigureAwait(false);
        if (!loaded.Succeeded)
            return loaded.Error;
        var (member, organization) = loaded.Value;
        if (organization.IsVerified)
            return new Error("organization.already_verified", "This organization's domain is already verified.");

        EmailAddress recipient;
        try
        {
            recipient = organization.DomainAdminMailbox(mailbox ?? string.Empty);
        }
        catch (DomainException ex)
        {
            return Error.From(ex);
        }

        var rawToken = magicLinks.Issue(MagicLinkPurpose.DomainVerification, recipient, organization.Id, member.Id, LinkLifetimes.DomainConfirmation);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await email.SendAsync(EmailTemplates.ConfirmDomain(recipient, organization, member.Email, links.MagicLink(rawToken)), cancellationToken)
            .ConfigureAwait(false);
        return recipient;
    }

    /// <summary>Redeems the link sent to the administrative mailbox. The person confirming need not be a member.</summary>
    public async Task<Result<DomainVerificationStatus>> ConfirmAdminMailboxAsync(string? rawToken, CancellationToken cancellationToken = default)
    {
        var consumed = await magicLinks.ConsumeAsync(rawToken, [MagicLinkPurpose.DomainVerification], cancellationToken).ConfigureAwait(false);
        if (!consumed.Succeeded)
            return consumed.Error;

        var token = consumed.Value;
        var organization = await db.Organizations.SingleAsync(o => o.Id == token.OrganizationId, cancellationToken).ConfigureAwait(false);
        if (organization.IsVerified)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Status(organization);
        }
        return await MarkVerifiedAsync(organization, DomainVerificationMethod.AdminMailbox, $"Confirmed by {token.Email}", cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<DomainVerificationStatus>> MarkVerifiedAsync(
        Organization organization,
        DomainVerificationMethod method,
        string evidence,
        CancellationToken cancellationToken)
    {
        var takenByAnother = await db.Organizations
            .AnyAsync(o => o.Domain == organization.Domain && o.Status == OrganizationStatus.Verified && o.Id != organization.Id, cancellationToken)
            .ConfigureAwait(false);
        if (takenByAnother)
            return Errors.DomainTaken(organization.Domain);

        organization.MarkVerified(method, evidence, time.UtcNow());
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Errors.LinkUsed;
        }
        catch (DbUpdateException ex) when (db.IsUniqueConstraintViolation(ex))
        {
            return Errors.DomainTaken(organization.Domain); // lost a race with another organization
        }
        return Status(organization);
    }

    private async Task<Result<StaffContext>> LoadPendingForAdminAsync(StaffActor actor, CancellationToken cancellationToken)
    {
        var context = await StaffContext.LoadAsync(db, actor, cancellationToken).ConfigureAwait(false);
        if (!context.Succeeded)
            return context.Error;
        return context.Value.Member.Role == MemberRole.Admin ? context : Errors.AdminRequired;
    }

    private static DomainVerificationStatus Status(Organization organization) => new(
        organization.Id,
        organization.Name,
        organization.Domain,
        organization.IsVerified,
        organization.VerificationMethod,
        organization.VerificationEvidence,
        organization.VerifiedAt,
        organization.DnsRecordName,
        organization.DnsRecordValue,
        Organization.DomainAdminMailboxes.Select(organization.DomainAdminMailbox).ToList());
}
