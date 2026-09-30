using System.ComponentModel.DataAnnotations;
using Chirograph.Application.Documents;
using Chirograph.Domain.Documents;
using Chirograph.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Chirograph.Web.Pages.Org.Documents;

[EnableRateLimiting(RateLimitPolicies.Issuance)]
public sealed class IssueModel(DocumentIssuanceService issuance) : PageModel
{
    [BindProperty]
    public IssueInput Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        await using var pdf = Input.Pdf!.OpenReadStream();
        var result = await issuance.IssueAsync(
            User.ToStaffActor(),
            new IssueDocumentCommand(
                Input.Type!.Value,
                Input.EmployeeName,
                Input.EmployeeEmail,
                Input.Designation,
                Input.EmploymentStart!.Value,
                Input.EmploymentEnd!.Value),
            pdf,
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error.Message);
            return Page();
        }

        TempData["Message"] = result.Value.EmailedToEmployee
            ? $"Issued and emailed to {Input.EmployeeEmail}."
            : "Issued, but the email to the employee could not be sent. Download the PDF below, or try resending.";
        return RedirectToPage("/Org/Documents/Details", new { id = result.Value.DocumentId });
    }

    public sealed class IssueInput
    {
        [Required(ErrorMessage = "Choose the PDF of the letter."), Display(Name = "Letter (PDF)")]
        public IFormFile? Pdf { get; set; }

        [Required(ErrorMessage = "Choose a document type."), Display(Name = "Document type")]
        public DocumentType? Type { get; set; }

        [Required, StringLength(DocumentDetails.EmployeeNameMaxLength), Display(Name = "Employee name")]
        public string? EmployeeName { get; set; }

        [Required, StringLength(DocumentDetails.DesignationMaxLength), Display(Name = "Role / designation")]
        public string? Designation { get; set; }

        [Required, EmailAddress, StringLength(254), Display(Name = "Employee's email")]
        public string? EmployeeEmail { get; set; }

        [Required, Display(Name = "Employment start date")]
        public DateOnly? EmploymentStart { get; set; }

        [Required, Display(Name = "Employment end date")]
        public DateOnly? EmploymentEnd { get; set; }
    }
}
