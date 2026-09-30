using Chirograph.Application.Common;
using Chirograph.Application.Organizations;
using Chirograph.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chirograph.Web.Pages.Org;

public sealed class DomainModel(DomainVerificationService domains) : PageModel
{
    public DomainVerificationStatus Status { get; private set; } = null!;

    [BindProperty]
    public string? Mailbox { get; set; }

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken) ? Page() : Forbid();

    public async Task<IActionResult> OnPostCheckDnsAsync(CancellationToken cancellationToken)
    {
        var result = await domains.CheckDnsAsync(User.ToStaffActor(), cancellationToken);
        if (result.Succeeded)
        {
            TempData["Message"] = $"{result.Value.Domain} is verified. You can now issue documents.";
            return RedirectToPage();
        }
        return await ShowErrorAsync(result.Error, cancellationToken);
    }

    public async Task<IActionResult> OnPostSendEmailAsync(CancellationToken cancellationToken)
    {
        var result = await domains.SendAdminMailboxLinkAsync(User.ToStaffActor(), Mailbox, cancellationToken);
        if (result.Succeeded)
        {
            TempData["Message"] = $"We sent a confirmation link to {result.Value}. Ask whoever reads that mailbox to open it; it expires in 24 hours.";
            return RedirectToPage();
        }
        return await ShowErrorAsync(result.Error, cancellationToken);
    }

    private async Task<IActionResult> ShowErrorAsync(Error error, CancellationToken cancellationToken)
    {
        ErrorMessage = error.Message;
        return await LoadAsync(cancellationToken) ? Page() : Forbid();
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var status = await domains.GetStatusAsync(User.ToStaffActor(), cancellationToken);
        if (!status.Succeeded)
            return false;
        Status = status.Value;
        return true;
    }
}
