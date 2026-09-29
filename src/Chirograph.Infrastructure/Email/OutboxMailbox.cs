using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Chirograph.Infrastructure.Email;

public sealed record OutboxMessageSummary(string Id, string To, string Subject, DateTimeOffset Date, int AttachmentCount);

public sealed record OutboxAttachment(string FileName, string ContentType, byte[] Content);

public sealed record OutboxMessage(OutboxMessageSummary Summary, string TextBody, IReadOnlyList<OutboxAttachment> Attachments);

/// <summary>Reads the development outbox for the /dev/mailbox page.</summary>
public sealed partial class OutboxMailbox(IOptions<EmailOptions> options)
{
    public async Task<IReadOnlyList<OutboxMessageSummary>> ListAsync(int max = 100, CancellationToken cancellationToken = default)
    {
        var directory = new DirectoryInfo(options.Value.OutboxDirectory);
        if (!directory.Exists)
            return [];

        var summaries = new List<OutboxMessageSummary>();
        foreach (var file in directory.EnumerateFiles("*.eml").OrderByDescending(f => f.Name, StringComparer.Ordinal).Take(max))
        {
            using var mime = await MimeMessage.LoadAsync(file.FullName, cancellationToken).ConfigureAwait(false);
            summaries.Add(Summarize(Path.GetFileNameWithoutExtension(file.Name), mime));
        }
        return summaries;
    }

    public async Task<OutboxMessage?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!IdPattern().IsMatch(id))
            return null;
        var path = Path.Combine(options.Value.OutboxDirectory, id + ".eml");
        if (!File.Exists(path))
            return null;

        using var mime = await MimeMessage.LoadAsync(path, cancellationToken).ConfigureAwait(false);
        var attachments = new List<OutboxAttachment>();
        foreach (var part in mime.Attachments.OfType<MimePart>().Where(part => part.Content is not null))
        {
            using var buffer = new MemoryStream();
            await part.Content!.DecodeToAsync(buffer, cancellationToken).ConfigureAwait(false);
            attachments.Add(new OutboxAttachment(part.FileName ?? "attachment", part.ContentType.MimeType, buffer.ToArray()));
        }
        return new OutboxMessage(Summarize(id, mime), mime.TextBody ?? string.Empty, attachments);
    }

    private static OutboxMessageSummary Summarize(string id, MimeMessage mime) =>
        new(id, mime.To.ToString(), mime.Subject ?? string.Empty, mime.Date, mime.Attachments.Count());

    [GeneratedRegex("^[0-9]{8}-[0-9]{6}-[0-9]{7}-[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
