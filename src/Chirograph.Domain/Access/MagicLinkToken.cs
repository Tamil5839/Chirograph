using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Chirograph.Domain.Common;

namespace Chirograph.Domain.Access;

/// <summary>
/// A single-use, expiring link sent by email. Only the SHA-256 of the 256-bit token is stored, so a copy of the
/// database cannot be turned into working links.
/// </summary>
public sealed class MagicLinkToken
{
    private const int TokenBytes = 32;

    private MagicLinkToken()
    {
    }

    public Guid Id { get; private set; }

    public MagicLinkPurpose Purpose { get; private set; }

    public string TokenHash { get; private set; } = null!;

    /// <summary>The mailbox the link was sent to; following it proves control of that mailbox.</summary>
    public EmailAddress Email { get; private set; } = null!;

    public Guid? OrganizationId { get; private set; }

    /// <summary>The member signing in or invited, or (for domain verification) the member who asked for it.</summary>
    public Guid? MemberId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <summary>Creates a token. The raw value is returned once, for the emailed link, and is never stored.</summary>
    public static (MagicLinkToken Token, string RawToken) Create(
        MagicLinkPurpose purpose,
        EmailAddress email,
        Guid? organizationId,
        Guid? memberId,
        TimeSpan lifetime,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        var needsMember = purpose is MagicLinkPurpose.StaffSignIn or MagicLinkPurpose.StaffInvite or MagicLinkPurpose.DomainVerification;
        if (needsMember && (organizationId is null || memberId is null))
            throw new ArgumentException($"A {purpose} link must identify the organization and member.");
        if (purpose == MagicLinkPurpose.EmployeeSignIn && (organizationId is not null || memberId is not null))
            throw new ArgumentException("An employee sign-in link is tied to an email address only.");

        var rawToken = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        var token = new MagicLinkToken
        {
            Id = Guid.CreateVersion7(now),
            Purpose = purpose,
            TokenHash = Hash(rawToken),
            Email = email,
            OrganizationId = organizationId,
            MemberId = memberId,
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        };
        return (token, rawToken);
    }

    public static string Hash(string rawToken)
    {
        ArgumentNullException.ThrowIfNull(rawToken);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }

    public bool IsUsable(DateTimeOffset now) => ConsumedAt is null && now < ExpiresAt;

    public void Consume(DateTimeOffset now)
    {
        if (ConsumedAt is not null)
            throw new DomainException("link.used", "This link has already been used. Request a new one.");
        if (now >= ExpiresAt)
            throw new DomainException("link.expired", "This link has expired. Request a new one.");
        ConsumedAt = now;
    }
}
