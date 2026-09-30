using Chirograph.Application.Abstractions;
using Chirograph.Application.Access;
using Chirograph.Application.Common;
using Chirograph.Domain.Access;
using Chirograph.Domain.Common;
using Chirograph.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Chirograph.Application.Organizations;

public sealed record TeamMemberView(
    Guid Id,
    EmailAddress Email,
    MemberRole Role,
    MemberStatus Status,
    DateTimeOffset InvitedAt,
    DateTimeOffset? ActivatedAt,
    bool IsYou);

/// <summary>Admins invite teammates with addresses on the verified domain, and remove them.</summary>
public sealed class TeamService(
    IAppDbContext db,
    MagicLinkService magicLinks,
    LinkBuilder links,
    IEmailSender email,
    TimeProvider time)
{
    public async Task<Result<IReadOnlyList<TeamMemberView>>> ListAsync(StaffActor actor, CancellationToken cancellationToken = default)
    {
        var context = await StaffContext.LoadAsync(db, actor, cancellationToken).ConfigureAwait(false);
        if (!context.Succeeded)
            return context.Error;

        var members = await db.OrganizationMembers
            .Where(m => m.OrganizationId == actor.OrganizationId && m.Status != MemberStatus.Removed)
            .OrderBy(m => m.InvitedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return members
            .Select(m => new TeamMemberView(m.Id, m.Email, m.Role, m.Status, m.InvitedAt, m.ActivatedAt, m.Id == actor.MemberId))
            .ToList();
    }

    public async Task<Result> InviteAsync(StaffActor actor, string? emailAddress, MemberRole role, CancellationToken cancellationToken = default)
    {
        var context = await StaffContext.LoadAsync(db, actor, cancellationToken).ConfigureAwait(false);
        if (!context.Succeeded)
            return context.Error;
        if (!EmailAddress.TryParse(emailAddress, out var inviteeEmail, out var error))
            return Errors.Validation(error);

        var (admin, organization) = context.Value;
        var now = time.UtcNow();
        var existing = await db.OrganizationMembers
            .SingleOrDefaultAsync(m => m.OrganizationId == organization.Id && m.Email == inviteeEmail, cancellationToken)
            .ConfigureAwait(false);
        OrganizationMember invitee;
        try
        {
            if (existing is null)
            {
                invitee = OrganizationMember.Invite(organization, admin, inviteeEmail, role, now);
                db.OrganizationMembers.Add(invitee);
            }
            else
            {
                existing.Reinvite(organization, admin, role, now);
                invitee = existing;
            }
        }
        catch (DomainException ex)
        {
            return Error.From(ex);
        }

        var rawToken = magicLinks.Issue(MagicLinkPurpose.StaffInvite, inviteeEmail, organization.Id, invitee.Id, LinkLifetimes.TeamInvite);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await email.SendAsync(EmailTemplates.TeamInvite(inviteeEmail, organization, admin.Email, role, links.MagicLink(rawToken)), cancellationToken)
            .ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> RemoveAsync(StaffActor actor, Guid memberId, CancellationToken cancellationToken = default)
    {
        var context = await StaffContext.LoadAsync(db, actor, cancellationToken).ConfigureAwait(false);
        if (!context.Succeeded)
            return context.Error;

        var (admin, organization) = context.Value;
        var target = await db.OrganizationMembers
            .SingleOrDefaultAsync(m => m.Id == memberId && m.OrganizationId == organization.Id, cancellationToken)
            .ConfigureAwait(false);
        if (target is null)
            return Errors.NotFound;

        try
        {
            target.Remove(organization, admin, time.UtcNow());
        }
        catch (DomainException ex)
        {
            return Error.From(ex);
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
