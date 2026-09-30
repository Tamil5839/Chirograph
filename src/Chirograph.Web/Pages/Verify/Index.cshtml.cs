using Chirograph.Domain.Documents;
using Chirograph.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Chirograph.Web.Pages.Verify;

[EnableRateLimiting(RateLimitPolicies.Verification)]
public sealed class IndexModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Id { get; set; }

    public bool Invalid { get; private set; }

    public IActionResult OnGet()
    {
        if (string.IsNullOrWhiteSpace(Id))
            return Page();
        if (VerificationId.TryParse(Id, out var id))
            return RedirectToPage("/Verify/Document", new { id = id.Value });

        Invalid = true;
        return Page();
    }
}
