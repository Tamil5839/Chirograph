using Chirograph.Application.Abstractions;
using Chirograph.Application.Access;
using Chirograph.Application.Common;
using Chirograph.Domain.Access;
using Chirograph.Domain.Common;
using Chirograph.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Chirograph.Application.Organizations;

public sealed record SignUpCommand(string? OrganizationName, string? Domain, string? Email);

public sealed record SignUpResult(Guid OrganizationId, EmailAddress Email);

/// <summary>
/// Registers an organization (pending until its domain is proven) and its founding admin, who must first confirm
/// their own mailbox on that domain.
/// </summary>
public sealed class OrganizationSignupService(
    IAppDbContext db,
    MagicLinkService magicLinks,
    LinkBuilder links,
    IEmailSender email,
    TimeProvider time)
{
    public async Task<Result<SignUpResult>> SignUpAsync(SignUpCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!DomainName.TryParse(command.Domain, out var domain, out var domainError))
            return Errors.Validation(domainError);
        if (!EmailAddress.TryParse(command.Email, out var founderEmail, out var emailError))
            return Errors.Validation(emailError);

        var now = time.UtcNow();
        Organization organization;
        OrganizationMember founder;
        try
        {
            organization = Organization.Register(command.OrganizationName ?? string.Empty, domain, now);
            if (!founderEmail.IsOn(domain))
                return Errors.Validation($"Use your work email address on {domain}, so we know you are part of the organization.");
            founder = OrganizationMember.CreateFounder(organization, founderEmail, now);
        }
        catch (DomainException ex)
        {
            return Error.From(ex);
        }

        var alreadyVerified = await db.Organizations
            .AnyAsync(o => o.Domain == domain && o.Status == OrganizationStatus.Verified, cancellationToken)
            .ConfigureAwait(false);
        if (alreadyVerified)
            return Errors.DomainTaken(domain);

        db.Organizations.Add(organization);
        db.OrganizationMembers.Add(founder);
        var rawToken = magicLinks.Issue(MagicLinkPurpose.StaffSignIn, founderEmail, organization.Id, founder.Id, LinkLifetimes.SignUpConfirmation);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await email.SendAsync(EmailTemplates.ConfirmSignUp(founderEmail, organization, links.MagicLink(rawToken)), cancellationToken)
            .ConfigureAwait(false);
        return new SignUpResult(organization.Id, founderEmail);
    }
}
