using Chirograph.Application.Access;
using Chirograph.Application.Common;
using Chirograph.Application.Employees;
using Chirograph.Application.Organizations;
using Chirograph.Domain.Access;
using Chirograph.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chirograph.Web.Pages.Auth;

/// <summary>
/// Landing page for every emailed link. GET only describes what the link will do; the link is used when the person
/// presses the button (POST), so mail scanners that open links automatically cannot use them up.
/// </summary>
public sealed class LinkModel(
    MagicLinkService links,
    StaffAuthService staff,
    DomainVerificationService domains,
    EmployeePortalService employees,
    TimeProvider time) : PageModel
{
    private static readonly TimeSpan EmployeeSessionLifetime = TimeSpan.FromHours(1);

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    public LinkPreview? Preview { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DomainVerificationStatus? ConfirmedDomain { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var preview = await links.PreviewAsync(Token, cancellationToken);
        if (preview.Succeeded)
            Preview = preview.Value;
        else
            ErrorMessage = preview.Error.Message;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var preview = await links.PreviewAsync(Token, cancellationToken);
        if (!preview.Succeeded)
            return Fail(preview.Error);

        switch (preview.Value.Purpose)
        {
            case MagicLinkPurpose.StaffSignIn or MagicLinkPurpose.StaffInvite:
                var session = await staff.CompleteSignInAsync(Token, cancellationToken);
                if (!session.Succeeded)
                    return Fail(session.Error);
                await HttpContext.SignInAsync(ChirographClaims.Scheme, ChirographClaims.ForStaff(session.Value));
                return session.Value.OrganizationVerified ? RedirectToPage("/Org/Index") : RedirectToPage("/Org/Domain");

            case MagicLinkPurpose.DomainVerification:
                var confirmed = await domains.ConfirmAdminMailboxAsync(Token, cancellationToken);
                if (!confirmed.Succeeded)
                    return Fail(confirmed.Error);
                ConfirmedDomain = confirmed.Value;
                return Page();

            case MagicLinkPurpose.EmployeeSignIn:
                var employee = await employees.CompleteSignInAsync(Token, cancellationToken);
                if (!employee.Succeeded)
                    return Fail(employee.Error);
                await HttpContext.SignInAsync(
                    ChirographClaims.Scheme,
                    ChirographClaims.ForEmployee(employee.Value),
                    new AuthenticationProperties { ExpiresUtc = time.GetUtcNow() + EmployeeSessionLifetime, AllowRefresh = false });
                return RedirectToPage("/Me/Index");

            default:
                return Fail(Errors.LinkInvalid);
        }
    }

    private PageResult Fail(Error error)
    {
        ErrorMessage = error.Message;
        return Page();
    }
}
