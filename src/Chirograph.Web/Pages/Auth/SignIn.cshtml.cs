using System.ComponentModel.DataAnnotations;
using Chirograph.Application.Access;
using Chirograph.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Chirograph.Web.Pages.Auth;

[EnableRateLimiting(RateLimitPolicies.EmailRequests)]
public sealed class SignInModel(StaffAuthService auth) : PageModel
{
    [BindProperty, Required, EmailAddress, StringLength(254), Display(Name = "Work email")]
    public string? Email { get; set; }

    public bool Sent { get; private set; }

    public IActionResult OnGet() => User.IsStaff() ? RedirectToPage("/Org/Index") : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        // Same response whether or not the address is known.
        await auth.RequestSignInLinkAsync(Email, cancellationToken);
        Sent = true;
        return Page();
    }
}
