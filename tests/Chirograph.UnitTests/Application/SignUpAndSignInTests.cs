using Chirograph.Application.Access;
using Chirograph.Application.Common;
using Chirograph.Application.Employees;
using Chirograph.Application.Organizations;
using Chirograph.Domain.Access;
using Chirograph.Domain.Organizations;
using Chirograph.UnitTests.Support;
using Microsoft.EntityFrameworkCore;

namespace Chirograph.UnitTests.Application;

public class SignUpAndSignInTests : ApplicationTestBase
{
    private Task<Result<SignUpResult>> SignUp(string name, string domain, string email) =>
        Host.RunAsync<OrganizationSignupService, Result<SignUpResult>>(s => s.SignUpAsync(new SignUpCommand(name, domain, email), Token));

    private Task<Result<StaffSession>> CompleteSignIn(string token) =>
        Host.RunAsync<StaffAuthService, Result<StaffSession>>(s => s.CompleteSignInAsync(token, Token));

    [Fact]
    public async Task Signing_up_creates_a_pending_organization_and_emails_the_founder_a_link()
    {
        var result = await SignUp("Acme Technologies", " ACME.test ", "Priya@Acme.test");

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal("priya@acme.test", result.Value.Email.Value);
        var email = Assert.Single(Host.Email.Sent);
        Assert.Equal("priya@acme.test", email.To.Value);
        Assert.Contains("Confirm your email", email.Subject, StringComparison.Ordinal);
        Assert.StartsWith($"{ApplicationTestHost.PublicBaseUrl}/auth/link?token=", email.TextBody.Split('\n').Single(l => l.Contains("/auth/link", StringComparison.Ordinal)).Trim(), StringComparison.Ordinal);

        var (organization, founder) = await Host.WithDatabaseAsync(async db =>
            (await db.Organizations.SingleAsync(Token), await db.OrganizationMembers.SingleAsync(Token)));
        Assert.Equal("acme.test", organization.Domain.Value);
        Assert.Equal(OrganizationStatus.PendingVerification, organization.Status);
        Assert.Equal(MemberRole.Admin, founder.Role);
        Assert.Equal(MemberStatus.Invited, founder.Status);
    }

    [Fact]
    public async Task The_founder_must_use_an_address_on_the_organizations_domain()
    {
        var result = await SignUp("Acme Technologies", "acme.test", "priya@gmail.com");

        Assert.False(result.Succeeded);
        Assert.Contains("acme.test", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(Host.Email.Sent);
        Assert.Equal(0, await Host.WithDatabaseAsync(db => db.Organizations.CountAsync(Token)));
    }

    [Fact]
    public async Task Public_email_domains_cannot_register()
    {
        var result = await SignUp("My Consultancy", "gmail.com", "me@gmail.com");

        Assert.Equal("organization.public_email_domain", result.Error?.Code);
    }

    [Fact]
    public async Task A_domain_verified_by_another_organization_cannot_be_registered_again()
    {
        await Host.CreateVerifiedOrganizationAsync();

        var result = await SignUp("Acme (fake)", "acme.test", "mallory@acme.test");

        Assert.Equal("organization.domain_taken", result.Error?.Code);
    }

    [Fact]
    public async Task Confirming_the_email_activates_the_founder_and_signs_them_in()
    {
        await SignUp("Acme Technologies", "acme.test", "priya@acme.test");

        var session = await CompleteSignIn(ApplicationTestHost.LinkToken(Host.Email.Last));

        Assert.True(session.Succeeded, session.Error?.Message);
        Assert.Equal("priya@acme.test", session.Value.Email.Value);
        Assert.Equal(MemberRole.Admin, session.Value.Role);
        Assert.Equal("Acme Technologies", session.Value.OrganizationName);
        Assert.False(session.Value.OrganizationVerified);
    }

    [Fact]
    public async Task A_link_can_be_previewed_without_being_used()
    {
        await SignUp("Acme Technologies", "acme.test", "priya@acme.test");
        var token = ApplicationTestHost.LinkToken(Host.Email.Last);

        var preview = await Host.RunAsync<MagicLinkService, Result<LinkPreview>>(s => s.PreviewAsync(token, Token));

        Assert.Equal(MagicLinkPurpose.StaffSignIn, preview.Value.Purpose);
        Assert.Equal("priya@acme.test", preview.Value.Email.Value);
        Assert.Equal("Acme Technologies", preview.Value.OrganizationName);
        Assert.True((await CompleteSignIn(token)).Succeeded);
    }

    [Fact]
    public async Task A_sign_in_link_works_only_once()
    {
        await SignUp("Acme Technologies", "acme.test", "priya@acme.test");
        var token = ApplicationTestHost.LinkToken(Host.Email.Last);
        await CompleteSignIn(token);

        var second = await CompleteSignIn(token);

        Assert.Equal("link.used", second.Error?.Code);
    }

    [Fact]
    public async Task A_sign_in_link_expires()
    {
        await SignUp("Acme Technologies", "acme.test", "priya@acme.test");
        Host.Time.Advance(LinkLifetimes.SignUpConfirmation + TimeSpan.FromSeconds(1));

        var result = await CompleteSignIn(ApplicationTestHost.LinkToken(Host.Email.Last));

        Assert.Equal("link.expired", result.Error?.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    public async Task Made_up_links_are_rejected(string token)
    {
        Assert.Equal("link.invalid", (await CompleteSignIn(token)).Error?.Code);
    }

    [Fact]
    public async Task A_staff_link_cannot_be_used_as_an_employee_link()
    {
        await SignUp("Acme Technologies", "acme.test", "priya@acme.test");

        var result = await Host.RunAsync<EmployeePortalService, Result<EmployeeSession>>(
            s => s.CompleteSignInAsync(ApplicationTestHost.LinkToken(Host.Email.Last), Token));

        Assert.Equal("link.invalid", result.Error?.Code);
    }

    [Fact]
    public async Task Asking_to_sign_in_with_an_unknown_address_sends_nothing()
    {
        await Host.RunAsync<StaffAuthService>(s => s.RequestSignInLinkAsync("stranger@acme.test", Token));

        Assert.Empty(Host.Email.Sent);
    }

    [Fact]
    public async Task A_member_of_several_organizations_gets_a_link_for_each()
    {
        await SignUp("Acme India", "acme.test", "priya@acme.test");
        await SignUp("Acme Labs", "acme.test", "priya@acme.test");

        await Host.RunAsync<StaffAuthService>(s => s.RequestSignInLinkAsync("priya@acme.test", Token));

        var email = Host.Email.Last;
        Assert.Contains("Acme India", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("Acme Labs", email.TextBody, StringComparison.Ordinal);
        Assert.Equal(2, email.TextBody.Split("/auth/link?token=").Length - 1);
    }
}
