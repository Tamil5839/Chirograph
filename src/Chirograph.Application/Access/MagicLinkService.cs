using Chirograph.Application.Abstractions;
using Chirograph.Application.Common;
using Chirograph.Domain.Access;
using Chirograph.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Chirograph.Application.Access;

/// <summary>What a magic link will do, shown on the confirmation page before the link is used.</summary>
public sealed record LinkPreview(
    MagicLinkPurpose Purpose,
    EmailAddress Email,
    string? OrganizationName,
    DomainName? OrganizationDomain,
    EmailAddress? RequestedBy);

/// <summary>
/// Issues and redeems single-use emailed links. Links are shown on a confirmation page (GET) and only consumed when
/// the person presses Continue (POST), so email security scanners that pre-fetch links cannot use them up.
/// </summary>
public sealed class MagicLinkService(IAppDbContext db, TimeProvider time)
{
    private const int MaxRawTokenLength = 128;

    /// <summary>Adds a new token to the unit of work (the caller saves) and returns the raw value for the link.</summary>
    public string Issue(MagicLinkPurpose purpose, EmailAddress email, Guid? organizationId, Guid? memberId, TimeSpan lifetime)
    {
        var (token, rawToken) = MagicLinkToken.Create(purpose, email, organizationId, memberId, lifetime, time.UtcNow());
        db.MagicLinkTokens.Add(token);
        return rawToken;
    }

    public async Task<Result<LinkPreview>> PreviewAsync(string? rawToken, CancellationToken cancellationToken = default)
    {
        var token = await FindAsync(rawToken, cancellationToken).ConfigureAwait(false);
        if (token is null)
            return Errors.LinkInvalid;
        if (token.ConsumedAt is not null)
            return Errors.LinkUsed;
        if (!token.IsUsable(time.UtcNow()))
            return new Error("link.expired", "This link has expired. Request a new one.");

        var organization = token.OrganizationId is null
            ? null
            : await db.Organizations.SingleOrDefaultAsync(o => o.Id == token.OrganizationId, cancellationToken).ConfigureAwait(false);
        var requestedBy = token.Purpose == MagicLinkPurpose.DomainVerification && token.MemberId is not null
            ? await db.OrganizationMembers.Where(m => m.Id == token.MemberId).Select(m => m.Email).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;
        return new LinkPreview(token.Purpose, token.Email, organization?.Name, organization?.Domain, requestedBy);
    }

    /// <summary>Marks the token used. The caller saves; a concurrent second use then fails with a concurrency error.</summary>
    public async Task<Result<MagicLinkToken>> ConsumeAsync(
        string? rawToken,
        IReadOnlyCollection<MagicLinkPurpose> allowedPurposes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allowedPurposes);
        var token = await FindAsync(rawToken, cancellationToken).ConfigureAwait(false);
        if (token is null || !allowedPurposes.Contains(token.Purpose))
            return Errors.LinkInvalid;

        try
        {
            token.Consume(time.UtcNow());
            return token;
        }
        catch (DomainException ex)
        {
            return Error.From(ex);
        }
    }

    private async Task<MagicLinkToken?> FindAsync(string? rawToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > MaxRawTokenLength)
            return null;
        var hash = MagicLinkToken.Hash(rawToken.Trim());
        return await db.MagicLinkTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken).ConfigureAwait(false);
    }
}
