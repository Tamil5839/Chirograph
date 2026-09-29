using Chirograph.Domain.Common;

namespace Chirograph.Application.Abstractions;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>A plain-text email. Chirograph sends no HTML, so nothing user-supplied can be rendered as markup.</summary>
public sealed record EmailMessage(EmailAddress To, string Subject, string TextBody)
{
    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];
}

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);
