using System.ComponentModel.DataAnnotations;
using Chirograph.Application.Organizations;
using Chirograph.Domain.Organizations;
using Chirograph.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chirograph.Web.Pages.Org;

public sealed class TeamModel(TeamService team) : PageModel
{
    [BindProperty]
    public InviteInput Invite { get; set; } = new();

    public IReadOnlyList<TeamMemberView> Members { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken) ? Page() : Forbid();

    public async Task<IActionResult> OnPostInviteAsync(CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await team.InviteAsync(User.ToStaffActor(), Invite.Email, Invite.Role, cancellationToken);
            if (result.Succeeded)
            {
                TempData["Message"] = $"Invitation sent to {Invite.Email}.";
                return RedirectToPage();
            }
            ModelState.AddModelError(string.Empty, result.Error.Message);
        }
        return await LoadAsync(cancellationToken) ? Page() : Forbid();
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var result = await team.RemoveAsync(User.ToStaffActor(), memberId, cancellationToken);
        if (result.Succeeded)
        {
            TempData["Message"] = "Member removed. They no longer have access.";
            return RedirectToPage();
        }
        ModelState.AddModelError(string.Empty, result.Error.Message);
        return await LoadAsync(cancellationToken) ? Page() : Forbid();
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var members = await team.ListAsync(User.ToStaffActor(), cancellationToken);
        if (!members.Succeeded)
            return false;
        Members = members.Value;
        return true;
    }

    public sealed class InviteInput
    {
        [Required, EmailAddress, StringLength(254), Display(Name = "Work email")]
        public string? Email { get; set; }

        [Display(Name = "Role")]
        public MemberRole Role { get; set; } = MemberRole.Issuer;
    }
}
