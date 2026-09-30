using Chirograph.Application.Access;
using Chirograph.Application.Common;
using Chirograph.Application.Organizations;
using Chirograph.Domain.Access;
using Chirograph.Domain.Common;
using Chirograph.Domain.Organizations;
using Chirograph.UnitTests.Support;
using Microsoft.EntityFrameworkCore;

namespace Chirograph.UnitTests.Application;

public class DomainVerificationTests : ApplicationTestBase
{
    private Task<Result<DomainVerificationStatus>> Status(StaffActor actor) =>
        Host.RunAsync<DomainVerificationService, Result<DomainVerificationStatus>>(s => s.GetStatusAsync(actor, Token));

    private Task<Result<DomainVerificationStatus>> CheckDns(StaffActor actor) =>
        Host.RunAsync<DomainVerificationService, Result<DomainVerificationStatus>>(s => s.CheckDnsAsync(actor, Token));

    private Task<Result<EmailAddress>> SendToMailbox(StaffActor actor, string mailbox) =>
        Host.RunAsync<DomainVerificationService, Result<EmailAddress>>(s => s.SendAdminMailboxLinkAsync(actor, mailbox, Token));

    private Task<Result<DomainVerificationStatus>> Confirm(string token) =>
        Host.RunAsync<DomainVerificationService, Result<DomainVerificationStatus>>(s => s.ConfirmAdminMailboxAsync(token, Token));

    [Fact]
    public async Task Publishing_the_TXT_record_verifies_the_domain()
    {
        var admin = await Host.SignUpAsync();
        var status = (await Status(admin)).Value;
        Host.Dns.Publish(status.DnsRecordName, status.DnsRecordValue);

        var result = await CheckDns(admin);

        Assert.True(result.Value.IsVerified);
        Assert.Equal(DomainVerificationMethod.DnsTxtRecord, result.Value.Method);
        Assert.Equal("TXT record at _chirograph.acme.test", result.Value.Evidence);
        Assert.Equal(TestData.Now, result.Value.VerifiedAt);
    }

    [Fact]
    public async Task The_record_may_also_be_published_on_the_bare_domain()
    {
        var admin = await Host.SignUpAsync();
        Host.Dns.Publish("acme.test", "v=spf1 -all");
        Host.Dns.Publish("acme.test", (await Status(admin)).Value.DnsRecordValue);

        var result = await CheckDns(admin);

        Assert.Equal("TXT record at acme.test", result.Value.Evidence);
    }

    [Fact]
    public async Task A_missing_or_wrong_record_leaves_the_domain_unverified()
    {
        var admin = await Host.SignUpAsync();
        Host.Dns.Publish("_chirograph.acme.test", "chirograph-verification=0123456789abcdef0123456789abcdef");

        var result = await CheckDns(admin);

        Assert.Equal("domain.dns_record_not_found", result.Error?.Code);
        Assert.False((await Status(admin)).Value.IsVerified);
    }

    [Fact]
    public async Task An_administrative_mailbox_on_the_domain_can_confirm_it()
    {
        var admin = await Host.SignUpAsync();

        var recipient = await SendToMailbox(admin, "hostmaster");

        Assert.Equal("hostmaster@acme.test", recipient.Value.Value);
        var email = Assert.Single(Host.Email.To("hostmaster@acme.test"));
        Assert.Contains("priya@acme.test", email.TextBody, StringComparison.Ordinal);
        var token = ApplicationTestHost.LinkToken(email);

        var preview = await Host.RunAsync<MagicLinkService, Result<LinkPreview>>(s => s.PreviewAsync(token, Token));
        Assert.Equal(MagicLinkPurpose.DomainVerification, preview.Value.Purpose);
        Assert.Equal("priya@acme.test", preview.Value.RequestedBy?.Value);

        var confirmed = await Confirm(token);
        Assert.True(confirmed.Value.IsVerified);
        Assert.Equal(DomainVerificationMethod.AdminMailbox, confirmed.Value.Method);
        Assert.Equal("Confirmed by hostmaster@acme.test", confirmed.Value.Evidence);
    }

    [Theory]
    [InlineData("priya")]
    [InlineData("hr")]
    public async Task Confirmation_cannot_be_sent_to_an_ordinary_mailbox(string mailbox)
    {
        var admin = await Host.SignUpAsync();
        var emailsBefore = Host.Email.Sent.Count;

        var result = await SendToMailbox(admin, mailbox);

        Assert.Equal("organization.invalid_admin_mailbox", result.Error?.Code);
        Assert.Equal(emailsBefore, Host.Email.Sent.Count);
    }

    [Fact]
    public async Task A_confirmation_link_works_only_once()
    {
        var admin = await Host.SignUpAsync();
        await SendToMailbox(admin, "admin");
        var token = ApplicationTestHost.LinkToken(Host.Email.To("admin@acme.test").Single());
        await Confirm(token);

        Assert.Equal("link.used", (await Confirm(token)).Error?.Code);
    }

    [Fact]
    public async Task Only_one_organization_can_ever_verify_a_domain()
    {
        var genuine = await Host.SignUpAsync(founder: "priya", name: "Acme Technologies");
        var impostor = await Host.SignUpAsync(founder: "mallory", name: "Acme HR Services");
        Host.Dns.Publish("_chirograph.acme.test", (await Status(impostor)).Value.DnsRecordValue);
        Host.Dns.Publish("_chirograph.acme.test", (await Status(genuine)).Value.DnsRecordValue);
        await SendToMailbox(impostor, "admin");
        var impostorLink = ApplicationTestHost.LinkToken(Host.Email.To("admin@acme.test").Single());

        Assert.True((await CheckDns(genuine)).Value.IsVerified);

        Assert.Equal("organization.domain_taken", (await CheckDns(impostor)).Error?.Code);
        Assert.Equal("organization.domain_taken", (await Confirm(impostorLink)).Error?.Code);
        var verifiedCount = await Host.WithDatabaseAsync(db => db.Organizations.CountAsync(o => o.Status == OrganizationStatus.Verified, Token));
        Assert.Equal(1, verifiedCount);
    }

    [Fact]
    public async Task A_founder_who_has_not_confirmed_their_mailbox_cannot_verify()
    {
        await Host.RunAsync<OrganizationSignupService, Result<SignUpResult>>(
            s => s.SignUpAsync(new SignUpCommand("Acme Technologies", "acme.test", "priya@acme.test"), Token));
        var founder = await Host.WithDatabaseAsync(db => db.OrganizationMembers.SingleAsync(Token));

        var result = await CheckDns(new StaffActor(founder.Id, founder.OrganizationId));

        Assert.Equal("forbidden", result.Error?.Code);
    }
}
