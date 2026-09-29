namespace Chirograph.Domain.Access;

public enum MagicLinkPurpose
{
    /// <summary>Signs an HR member in (and activates a founder's membership on first use).</summary>
    StaffSignIn,

    /// <summary>Accepts an invitation to join an organization's team.</summary>
    StaffInvite,

    /// <summary>Sent to an administrative mailbox on the domain to confirm the organization controls it.</summary>
    DomainVerification,

    /// <summary>Signs an employee in to see their documents and who verified them.</summary>
    EmployeeSignIn,
}
