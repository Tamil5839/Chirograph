using System.ComponentModel.DataAnnotations;
using Chirograph.Application.Organizations;
using Chirograph.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Chirograph.Web.Pages;

[EnableRateLimiting(RateLimitPolicies.EmailRequests)]
public sealed class SignUpModel(OrganizationSignupService signup) : PageModel
{
    [BindProperty]
    public SignUpInput Input { get; set; } = new();

    public string? SentTo { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        var result = await signup.SignUpAsync(new SignUpCommand(Input.OrganizationName, Input.Domain, Input.Email), cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error.Message);
            return Page();
        }
        SentTo = result.Value.Email.Value;
        return Page();
    }

    public sealed class SignUpInput
    {
        [Required, StringLength(200), Display(Name = "Organization name")]
        public string? OrganizationName { get; set; }

        [Required, StringLength(253), Display(Name = "Company domain")]
        public string? Domain { get; set; }

        [Required, EmailAddress, StringLength(254), Display(Name = "Your work email")]
        public string? Email { get; set; }
    }
}
