using Chirograph.Application.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;

namespace Chirograph.Infrastructure.Email;

public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;
        using var mime = MimeMessageFactory.Create(message, settings);
        using var client = new SmtpClient();

        await client.ConnectAsync(
            settings.Smtp.Host,
            settings.Smtp.Port,
            Enum.Parse<SecureSocketOptions>(settings.Smtp.Security, ignoreCase: true),
            cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(settings.Smtp.Username))
            await client.AuthenticateAsync(settings.Smtp.Username, settings.Smtp.Password ?? string.Empty, cancellationToken).ConfigureAwait(false);
        await client.SendAsync(mime, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
    }
}
