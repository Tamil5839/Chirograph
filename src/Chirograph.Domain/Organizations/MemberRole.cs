namespace Chirograph.Domain.Organizations;

public enum MemberRole
{
    /// <summary>Manages the domain and the team, and can issue and revoke documents.</summary>
    Admin,

    /// <summary>Can issue and revoke documents.</summary>
    Issuer,
}
