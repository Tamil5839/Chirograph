using System.ComponentModel.DataAnnotations;
using Chirograph.Application.Verification;
using Chirograph.Domain.Documents;
using Chirograph.Domain.Verification;
using Chirograph.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Chirograph.Web.Pages.Verify;

/// <summary>The public verification page reached from the QR code: /v/{verification ID}.</summary>
[EnableRateLimiting(RateLimitPolicies.Verification)]
public sealed class DocumentModel(VerificationService verification) : PageModel
{
    [FromRoute]
    public string Id { get; set; } = string.Empty;

    [BindProperty, Display(Name = "PDF you received")]
    public IFormFile? Upload { get; set; }

    [BindProperty, StringLength(200), Display(Name = "Your organization")]
    public string? VerifierOrganization { get; set; }

    public VerificationResult Result { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        // Typed IDs (lower case, hyphens, O for 0) are redirected to one canonical address before anything is logged.
        if (VerificationId.TryParse(Id, out var parsed) && parsed.Value != Id)
            return RedirectToPage(new { id = parsed.Value });

        Result = await verification.ViewAsync(Id, cancellationToken);
        return Respond();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (Upload is null || Upload.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Choose the PDF file you received.");
            Result = await verification.ViewAsync(Id, cancellationToken);
            return Respond();
        }

        await using var content = Upload.OpenReadStream();
        Result = await verification.CheckFileAsync(Id, content, VerifierOrganization, cancellationToken);
        return Respond();
    }

    private PageResult Respond()
    {
        if (Result.Outcome.Verdict == VerificationVerdict.NotFound)
            Response.StatusCode = StatusCodes.Status404NotFound;
        return Page();
    }
}
