namespace Chirograph.Domain.Organizations;

public enum DomainVerificationMethod
{
    /// <summary>A TXT record containing the organization's challenge was found in the domain's DNS.</summary>
    DnsTxtRecord,

    /// <summary>Someone holding an administrative mailbox on the domain (admin@, hostmaster@, …) confirmed a link.</summary>
    AdminMailbox,
}
