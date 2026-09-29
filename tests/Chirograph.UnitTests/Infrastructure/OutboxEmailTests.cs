using Chirograph.Application.Abstractions;
using Chirograph.Infrastructure.Email;
using Chirograph.UnitTests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Chirograph.UnitTests.Infrastructure;

public sealed class OutboxEmailTests : IDisposable
{
    private readonly TemporaryDirectory _directory = new();
    private readonly IOptions<EmailOptions> _options;

    public OutboxEmailTests() =>
        _options = Options.Create(new EmailOptions { OutboxDirectory = _directory.Path, FromAddress = "no-reply@chirograph.test" });

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task Sent_mail_can_be_read_back_from_the_outbox_with_its_attachment()
    {
        var sender = new OutboxEmailSender(_options, NullLogger<OutboxEmailSender>.Instance);
        var pdf = "%PDF-1.7 letter"u8.ToArray();

        await sender.SendAsync(
            new EmailMessage(TestData.Email("anita@example.org"), "Your experience letter", "Hello Anita,\nYour letter is attached.")
            {
                Attachments = [new EmailAttachment("experience-letter.pdf", "application/pdf", pdf)],
            },
            Token);

        var mailbox = new OutboxMailbox(_options);
        var summary = Assert.Single(await mailbox.ListAsync(cancellationToken: Token));
        Assert.Equal("anita@example.org", summary.To);
        Assert.Equal("Your experience letter", summary.Subject);
        Assert.Equal(1, summary.AttachmentCount);

        var message = await mailbox.GetAsync(summary.Id, Token);
        Assert.NotNull(message);
        Assert.Contains("Your letter is attached.", message.TextBody, StringComparison.Ordinal);
        var attachment = Assert.Single(message.Attachments);
        Assert.Equal("experience-letter.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(pdf, attachment.Content);
    }

    [Fact]
    public async Task The_newest_mail_is_listed_first()
    {
        var sender = new OutboxEmailSender(_options, NullLogger<OutboxEmailSender>.Instance);
        await sender.SendAsync(new EmailMessage(TestData.Email("a@example.org"), "First", "1"), Token);
        await sender.SendAsync(new EmailMessage(TestData.Email("b@example.org"), "Second", "2"), Token);

        var summaries = await new OutboxMailbox(_options).ListAsync(cancellationToken: Token);

        Assert.Equal(["Second", "First"], summaries.Select(s => s.Subject));
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("not-an-id")]
    public async Task Only_outbox_message_ids_can_be_opened(string id)
    {
        Assert.Null(await new OutboxMailbox(_options).GetAsync(id, Token));
    }
}
