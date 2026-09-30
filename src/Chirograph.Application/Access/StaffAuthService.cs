using Chirograph.Application.Abstractions;
using Chirograph.Application.Common;
using Chirograph.Domain.Access;
using Chirograph.Domain.Common;
using Chirograph.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Chirograph.Application.Access;

/// <summary>Passwordless sign-in for HR members: an emailed single-use link proves control of the mailbox.</summary>
public sealed class StaffAuthService(
    IAppDbContext db,
    MagicLinkService magicLinks,
    LinkBuilder links,
    IEmailSender email,
    TimeProvider time)
{
    private const int MaxOrganizationsPerEmail = 10;
    private static readonly MagicLinkPurpose[] SignInPurposes = [MagicLinkPurpose.StaffSignIn, MagicLinkPurpose.StaffInvite];

    /// <summary>
    /// Emails a sign-in link for each organization the address belongs to. Deliberately gives no indication of
    /// whether the address is known, so it cannot be used to discover who works where.
    /// </summary>
    public async Task RequestSignInLinkAsync(string? emailAddress, CancellationToken cancellationToken = default)
    {
        if (!EmailAddress.TryParse(emailAddress, out var address, out _))
            return;

        var memberships = await (
                from member in db.OrganizationMembers
                join organization in db.Organizations on member.OrganizationId equals organization.Id
                where member.Email == address && member.Status != MemberStatus.Removed
                orderby organization.CreatedAt descending
                select new { member.Id, member.OrganizationId, organization.Name, organization.Domain })
            .Take(MaxOrganizationsPerEmail)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (memberships.Count == 0)
            return;

        var signInLinks = memberships
            .Select(m => ($"{m.Name} ({m.Domain})",
                links.MagicLink(magicLinks.Issue(MagicLinkPurpose.StaffSignIn, address, m.OrganizationId, m.Id, LinkLifetimes.StaffSignIn))))
            .ToList();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await email.SendAsync(EmailTemplates.StaffSignIn(address, signInLinks), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Redeems a sign-in or invitation link; the first use activates the membership.</summary>
    public async Task<Result<StaffSession>> CompleteSignInAsync(string? rawToken, CancellationToken cancellationToken = default)
    {
        var consumed = await magicLinks.ConsumeAsync(rawToken, SignInPurposes, cancellationToken).ConfigureAwait(false);
        if (!consumed.Succeeded)
            return consumed.Error;

        var token = consumed.Value;
        var member = await db.OrganizationMembers.SingleOrDefaultAsync(m => m.Id == token.MemberId, cancellationToken).ConfigureAwait(false);
        if (member is null || member.Status == MemberStatus.Removed)
            return Errors.NotAnActiveMember;

        member.Activate(time.UtcNow());
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Errors.LinkUsed;
        }

        var session = await GetSessionAsync(member.Id, cancellationToken).ConfigureAwait(false);
        return session is null ? Errors.NotAnActiveMember : session;
    }

    /// <summary>Current session details for an active member, or <c>null</c> if the member was removed.</summary>
    public async Task<StaffSession?> GetSessionAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var row = await (
                from member in db.OrganizationMembers
                join organization in db.Organizations on member.OrganizationId equals organization.Id
                where member.Id == memberId && member.Status == MemberStatus.Active
                select new { member, organization })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return row is null
            ? null
            : new StaffSession(
                row.member.Id,
                row.organization.Id,
                row.member.Email,
                row.member.Role,
                row.organization.Name,
                row.organization.Domain,
                row.organization.IsVerified);
    }
}
