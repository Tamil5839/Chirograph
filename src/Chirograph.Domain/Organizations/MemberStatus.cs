namespace Chirograph.Domain.Organizations;

public enum MemberStatus
{
    /// <summary>Invited (or registered the organization) but has not yet proven control of the mailbox.</summary>
    Invited,
    Active,
    Removed,
}
