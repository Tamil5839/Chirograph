using Chirograph.Application.Abstractions;
using MimeKit;

namespace Chirograph.Infrastructure.Email;

internal static class MimeMessageFactory
{
    public static MimeMessage Create(EmailMessage message, EmailOptions options)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To.Value));
        mime.Subject = message.Subject;

        var body = new BodyBuilder { TextBody = message.TextBody };
        foreach (var attachment in message.Attachments)
            body.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        mime.Body = body.ToMessageBody();
        return mime;
    }
}
