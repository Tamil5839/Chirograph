using System.ComponentModel.DataAnnotations;
using Chirograph.Application.Employees;
using Chirograph.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Chirograph.Web.Pages.Me;

[EnableRateLimiting(RateLimitPolicies.EmailRequests)]
public sealed class IndexModel(EmployeePortalService portal) : PageModel
{
    [BindProperty, Required, EmailAddress, StringLength(254), Display(Name = "Your email address")]
    public string? Email { get; set; }

    public bool Sent { get; private set; }

    /// <summary>Set when an employee is signed in.</summary>
    public IReadOnlyList<EmployeeDocumentSummary>? Documents { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (User.IsEmployee())
            Documents = await portal.ListDocumentsAsync(User.ToEmployeeSession(), cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        // Same response whether or not the address has documents.
        await portal.RequestSignInLinkAsync(Email, cancellationToken);
        Sent = true;
        return Page();
    }
}
