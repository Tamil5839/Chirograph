using Chirograph.Application.Abstractions;
using Chirograph.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Chirograph.Application.Common;

/// <summary>The acting member and their organization, loaded fresh for every use case.</summary>
internal sealed record StaffContext(OrganizationMember Member, Organization Organization)
{
    /// <summary>
    /// Re-checks membership against the database rather than trusting the session, so a removed member loses access
    /// immediately.
    /// </summary>
    public static async Task<Result<StaffContext>> LoadAsync(IAppDbContext db, StaffActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var member = await db.OrganizationMembers
            .SingleOrDefaultAsync(m => m.Id == actor.MemberId && m.OrganizationId == actor.OrganizationId, cancellationToken)
            .ConfigureAwait(false);
        if (member is null || !member.IsActive)
            return Errors.NotAnActiveMember;

        var organization = await db.Organizations.SingleAsync(o => o.Id == actor.OrganizationId, cancellationToken).ConfigureAwait(false);
        return new StaffContext(member, organization);
    }
}
