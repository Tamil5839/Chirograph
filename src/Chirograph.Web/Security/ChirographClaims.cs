using System.Security.Claims;
using Chirograph.Application.Common;
using Chirograph.Domain.Common;
using Chirograph.Domain.Organizations;

namespace Chirograph.Web.Security;

/// <summary>What the auth cookie carries. Staff membership is re-validated against the database on every request.</summary>
public static class ChirographClaims
{
    public const string Scheme = "Chirograph";
    public const string Kind = "chirograph:kind";
    public const string StaffKind = "staff";
    public const string EmployeeKind = "employee";
    public const string OrganizationId = "chirograph:org";
    public const string OrganizationName = "chirograph:org_name";
    public const string OrganizationDomain = "chirograph:org_domain";
    public const string OrganizationVerified = "chirograph:org_verified";

    public static ClaimsPrincipal ForStaff(StaffSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Principal(
            new Claim(Kind, StaffKind),
            new Claim(ClaimTypes.NameIdentifier, session.MemberId.ToString()),
            new Claim(ClaimTypes.Email, session.Email.Value),
            new Claim(ClaimTypes.Role, session.Role.ToString()),
            new Claim(OrganizationId, session.OrganizationId.ToString()),
            new Claim(OrganizationName, session.OrganizationName),
            new Claim(OrganizationDomain, session.OrganizationDomain.Value),
            new Claim(OrganizationVerified, session.OrganizationVerified ? "true" : "false"));
    }

    public static ClaimsPrincipal ForEmployee(EmployeeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Principal(new Claim(Kind, EmployeeKind), new Claim(ClaimTypes.Email, session.Email.Value));
    }

    public static bool IsStaff(this ClaimsPrincipal user) => user.FindFirstValue(Kind) == StaffKind;

    public static bool IsEmployee(this ClaimsPrincipal user) => user.FindFirstValue(Kind) == EmployeeKind;

    public static bool IsOrganizationAdmin(this ClaimsPrincipal user) => user.IsStaff() && user.IsInRole(nameof(MemberRole.Admin));

    public static bool OrganizationIsVerified(this ClaimsPrincipal user) => user.FindFirstValue(OrganizationVerified) == "true";

    public static StaffActor ToStaffActor(this ClaimsPrincipal user) =>
        new(Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), Guid.Parse(user.FindFirstValue(OrganizationId)!));

    public static EmployeeSession ToEmployeeSession(this ClaimsPrincipal user) =>
        new(EmailAddress.Parse(user.FindFirstValue(ClaimTypes.Email)));

    /// <summary>True when the cookie's claims no longer reflect the member's current role or organization state.</summary>
    public static bool IsStaleFor(this ClaimsPrincipal user, StaffSession session) =>
        user.FindFirstValue(ClaimTypes.Role) != session.Role.ToString()
        || user.FindFirstValue(OrganizationVerified) != (session.OrganizationVerified ? "true" : "false")
        || user.FindFirstValue(OrganizationName) != session.OrganizationName;

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, Scheme, ClaimTypes.Email, ClaimTypes.Role));
}
