using System.ComponentModel.DataAnnotations;
using Chirograph.Application.Documents;
using Chirograph.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chirograph.Web.Pages.Org.Documents;

public sealed class DetailsModel(IssuerDocumentsService documents, DocumentIssuanceService issuance) : PageModel
{
    public IssuerDocumentView Document { get; private set; } = null!;

    [BindProperty, Display(Name = "Reason (required)")]
    public string? Reason { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken) =>
        await LoadAsync(id, cancellationToken) ? Page() : NotFound();

    public async Task<IActionResult> OnGetDownloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var file = await documents.OpenFileAsync(User.ToStaffActor(), id, cancellationToken);
        return file.Succeeded ? File(file.Value.Content, file.Value.ContentType, file.Value.FileName) : NotFound();
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await documents.RevokeAsync(User.ToStaffActor(), id, Reason, cancellationToken);
        if (result.Succeeded)
        {
            TempData["Message"] = "Document revoked. Verifiers now see it as revoked, and the employee has been told.";
            return RedirectToPage(new { id });
        }
        if (result.Error.Code == "not_found")
            return NotFound();
        ModelState.AddModelError(string.Empty, result.Error.Message);
        return await LoadAsync(id, cancellationToken) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostResendAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await issuance.ResendToEmployeeAsync(User.ToStaffActor(), id, cancellationToken);
        if (result.Succeeded)
        {
            TempData["Message"] = "Sent to the employee again.";
            return RedirectToPage(new { id });
        }
        if (result.Error.Code == "not_found")
            return NotFound();
        ModelState.AddModelError(string.Empty, result.Error.Message);
        return await LoadAsync(id, cancellationToken) ? Page() : NotFound();
    }

    private async Task<bool> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await documents.GetAsync(User.ToStaffActor(), id, cancellationToken);
        if (!document.Succeeded)
            return false;
        Document = document.Value;
        return true;
    }
}
