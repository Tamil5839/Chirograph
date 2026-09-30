using Chirograph.Domain.Common;
using Chirograph.Domain.Organizations;

namespace Chirograph.Application.Common;

/// <summary>The signed-in HR member on whose behalf a use case runs (taken from the auth cookie).</summary>
public sealed record StaffActor(Guid MemberId, Guid OrganizationId);

/// <summary>What the web layer keeps in a staff member's session.</summary>
public sealed record StaffSession(
    Guid MemberId,
    Guid OrganizationId,
    EmailAddress Email,
    MemberRole Role,
    string OrganizationName,
    DomainName OrganizationDomain,
    bool OrganizationVerified);

/// <summary>An employee signed in with a magic link sent to <see cref="Email"/>.</summary>
public sealed record EmployeeSession(EmailAddress Email);

/// <summary>A stored PDF opened for download. The caller disposes <see cref="Content"/>.</summary>
public sealed record StoredFile(Stream Content, string FileName, string ContentType = "application/pdf");
