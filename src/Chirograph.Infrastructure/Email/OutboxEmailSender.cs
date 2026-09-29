using Chirograph.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chirograph.Infrastructure.Email;

/// <summary>
/// Development sender: each email becomes an .eml file in the outbox folder, readable in any mail client or on the
/// app's /dev/mailbox page. Makes the whole sign-up, verification and issuing flow work without an SMTP server.
/// </summary>
public sealed partial class OutboxEmailSender(IOptions<EmailOptions> options, ILogger<OutboxEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var directory = options.Value.OutboxDirectory;
        Directory.CreateDirectory(directory);

        var fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.eml";
        using var mime = MimeMessageFactory.Create(message, options.Value);
        await mime.WriteToAsync(Path.Combine(directory, fileName), cancellationToken).ConfigureAwait(false);
        LogWritten(logger, fileName);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email written to the development outbox as {FileName}")]
    private static partial void LogWritten(ILogger logger, string fileName);
}
