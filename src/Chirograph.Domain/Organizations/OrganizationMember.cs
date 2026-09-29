using Chirograph.Domain.Common;

namespace Chirograph.Domain.Organizations;

/// <summary>
/// An HR user. Only an email address on the organization's domain and a role are kept; no names or phone numbers.
/// </summary>
public sealed class OrganizationMember
{
    private OrganizationMember()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public EmailAddress Email { get; private set; } = null!;

    public MemberRole Role { get; private set; }

    public MemberStatus Status { get; private set; }

    public DateTimeOffset InvitedAt { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public DateTimeOffset? RemovedAt { get; private set; }

    public bool IsActive => Status == MemberStatus.Active;

    public bool IsActiveAdmin => IsActive && Role == MemberRole.Admin;

    /// <summary>The person registering an organization becomes its first admin once they confirm their mailbox.</summary>
    public static OrganizationMember CreateFounder(Organization organization, EmailAddress email, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(organization);
        organization.EnsureOwnsEmail(email);
        return Create(organization.Id, email, MemberRole.Admin, now);
    }

    public static OrganizationMember Invite(
        Organization organization,
        OrganizationMember invitedBy,
        EmailAddress email,
        MemberRole role,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(organization);
        EnsureCanManageTeam(organization, invitedBy);
        organization.EnsureOwnsEmail(email);
        EnsureDefined(role);
        return Create(organization.Id, email, role, now);
    }

    /// <summary>Brings back a previously removed member with a fresh invitation.</summary>
    public void Reinvite(Organization organization, OrganizationMember invitedBy, MemberRole role, DateTimeOffset now)
    {
        EnsureCanManageTeam(organization, invitedBy);
        EnsureDefined(role);
        if (Status != MemberStatus.Removed)
            throw new DomainException("team.already_member", $"{Email} is already a member of this organization.");

        Role = role;
        Status = MemberStatus.Invited;
        InvitedAt = now;
        ActivatedAt = null;
        RemovedAt = null;
    }

    /// <summary>Called once the member has proven control of their mailbox by following a sign-in link.</summary>
    public void Activate(DateTimeOffset now)
    {
        if (Status == MemberStatus.Removed)
            throw new DomainException("member.removed", "This account has been removed from the organization.");
        if (Status == MemberStatus.Active)
            return;

        Status = MemberStatus.Active;
        ActivatedAt = now;
    }

    public void Remove(Organization organization, OrganizationMember removedBy, DateTimeOffset now)
    {
        EnsureCanManageTeam(organization, removedBy);
        if (removedBy.Id == Id)
            throw new DomainException("team.cannot_remove_self", "You cannot remove yourself. Ask another admin.");
        if (OrganizationId != organization.Id)
            throw new DomainException("member.wrong_organization", "This person is not a member of your organization.");
        if (Status == MemberStatus.Removed)
            return;

        Status = MemberStatus.Removed;
        RemovedAt = now;
    }

    /// <summary>Admins and issuers may issue and revoke documents, but only for their own organization.</summary>
    public void EnsureCanActFor(Guid organizationId)
    {
        if (OrganizationId != organizationId)
        {
            throw new DomainException(
                "member.wrong_organization",
                "You can only manage documents issued by your own organization.");
        }
        if (!IsActive)
            throw new DomainException("member.inactive", "Your account is not active in this organization.");
    }

    private static void EnsureCanManageTeam(Organization organization, OrganizationMember actor)
    {
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(actor);
        actor.EnsureCanActFor(organization.Id);
        if (actor.Role != MemberRole.Admin)
            throw new DomainException("team.admin_required", "Only admins can manage the team.");
        if (!organization.IsVerified)
        {
            throw new DomainException(
                "team.organization_not_verified",
                "Verify your organization's domain before managing the team.");
        }
    }

    private static void EnsureDefined(MemberRole role)
    {
        if (!Enum.IsDefined(role))
            throw new DomainException("team.invalid_role", "Choose a role.");
    }

    private static OrganizationMember Create(Guid organizationId, EmailAddress email, MemberRole role, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        return new OrganizationMember
        {
            Id = Guid.CreateVersion7(now),
            OrganizationId = organizationId,
            Email = email,
            Role = role,
            Status = MemberStatus.Invited,
            InvitedAt = now,
        };
    }
}
