using Chirograph.Infrastructure.Email;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Chirograph.Web.Pages.Dev;

/// <summary>Reads the development email outbox. Returns 404 outside Development or when real email is configured.</summary>
public sealed class MailboxModel(OutboxMailbox mailbox, IWebHostEnvironment environment, IOptions<EmailOptions> email) : PageModel
{
    public IReadOnlyList<OutboxMessageSummary> Messages { get; private set; } = [];

    public OutboxMessage? Selected { get; private set; }

    public string OutboxDirectory => email.Value.OutboxDirectory;

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!environment.IsDevelopment() || email.Value.Delivery != EmailDelivery.Outbox)
            context.Result = NotFound();
    }

    public async Task OnGetAsync(string? id, CancellationToken cancellationToken)
    {
        Messages = await mailbox.ListAsync(cancellationToken: cancellationToken);
        var selectedId = id ?? Messages.FirstOrDefault()?.Id;
        if (selectedId is not null)
            Selected = await mailbox.GetAsync(selectedId, cancellationToken);
    }

    public async Task<IActionResult> OnGetAttachmentAsync(string id, int index, CancellationToken cancellationToken)
    {
        var message = await mailbox.GetAsync(id, cancellationToken);
        if (message is null || index < 0 || index >= message.Attachments.Count)
            return NotFound();
        var attachment = message.Attachments[index];
        return File(attachment.Content, attachment.ContentType, attachment.FileName);
    }
}
