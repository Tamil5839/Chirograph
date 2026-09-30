using Chirograph.Application.Documents;
using Chirograph.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chirograph.Web.Pages.Org;

public sealed class IndexModel(IssuerDocumentsService documents) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public IReadOnlyList<DocumentSummary> Documents { get; private set; } = [];

    public IReadOnlyList<RecentVerification> Recent { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var actor = User.ToStaffActor();
        var list = await documents.ListAsync(actor, Q, cancellationToken);
        if (!list.Succeeded)
            return Forbid();
        Documents = list.Value;
        Recent = (await documents.RecentVerificationsAsync(actor, 10, cancellationToken)).Value;
        return Page();
    }
}
