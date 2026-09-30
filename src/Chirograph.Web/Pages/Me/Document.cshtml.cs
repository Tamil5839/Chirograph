using Chirograph.Application.Employees;
using Chirograph.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chirograph.Web.Pages.Me;

public sealed class DocumentModel(EmployeePortalService portal) : PageModel
{
    public EmployeeDocumentView Document { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await portal.GetDocumentAsync(User.ToEmployeeSession(), id, cancellationToken);
        if (!document.Succeeded)
            return NotFound();
        Document = document.Value;
        return Page();
    }

    public async Task<IActionResult> OnGetDownloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var file = await portal.OpenFileAsync(User.ToEmployeeSession(), id, cancellationToken);
        return file.Succeeded ? File(file.Value.Content, file.Value.ContentType, file.Value.FileName) : NotFound();
    }
}
