using Chirograph.Application.Access;
using Chirograph.Application.Common;
using Chirograph.Application.Documents;
using Chirograph.Application.Organizations;
using Chirograph.Domain.Organizations;
using Chirograph.UnitTests.Support;

namespace Chirograph.UnitTests.Application;

public class TeamTests : ApplicationTestBase
{
    private Task<Result> Invite(StaffActor admin, string email, MemberRole role = MemberRole.Issuer) =>
        Host.RunAsync<TeamService, Result>(s => s.InviteAsync(admin, email, role, Token));

    private async Task<StaffActor> InviteAndAccept(StaffActor admin, string email, MemberRole role = MemberRole.Issuer)
    {
        Assert.True((await Invite(admin, email, role)).Succeeded);
        var session = await Host.RunAsync<StaffAuthService, Result<StaffSession>>(
            s => s.CompleteSignInAsync(ApplicationTestHost.LinkToken(Host.Email.To(email).Last()), Token));
        Assert.True(session.Succeeded, session.Error?.Message);
        return new StaffActor(session.Value.MemberId, session.Value.OrganizationId);
    }

    [Fact]
    public async Task An_invited_teammate_accepts_and_can_issue_documents()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();

        var ravi = await InviteAndAccept(admin, "ravi@acme.test");

        var invitation = Assert.Single(Host.Email.To("ravi@acme.test"));
        Assert.Contains("priya@acme.test invited you", invitation.TextBody, StringComparison.Ordinal);
        Assert.True((await Host.IssueAsync(ravi)).Succeeded);
        var session = await Host.RunAsync<StaffAuthService, StaffSession?>(s => s.GetSessionAsync(ravi.MemberId, Token));
        Assert.Equal(MemberRole.Issuer, session?.Role);
    }

    [Fact]
    public async Task Invitations_are_only_for_addresses_on_the_verified_domain()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();

        var result = await Invite(admin, "ravi@gmail.com");

        Assert.Equal("organization.email_not_on_domain", result.Error?.Code);
    }

    [Fact]
    public async Task Nobody_can_be_invited_before_the_domain_is_verified()
    {
        var founder = await Host.SignUpAsync();

        var result = await Invite(founder, "ravi@acme.test");

        Assert.Equal("team.organization_not_verified", result.Error?.Code);
    }

    [Fact]
    public async Task Issuers_cannot_manage_the_team()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var ravi = await InviteAndAccept(admin, "ravi@acme.test");

        Assert.Equal("team.admin_required", (await Invite(ravi, "meera@acme.test")).Error?.Code);
        Assert.Equal("team.admin_required", (await Host.RunAsync<TeamService, Result>(s => s.RemoveAsync(ravi, admin.MemberId, Token))).Error?.Code);
    }

    [Fact]
    public async Task A_removed_member_loses_access_immediately()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var ravi = await InviteAndAccept(admin, "ravi@acme.test");
        var emailsToRavi = Host.Email.To("ravi@acme.test").Count;

        var removed = await Host.RunAsync<TeamService, Result>(s => s.RemoveAsync(admin, ravi.MemberId, Token));

        Assert.True(removed.Succeeded);
        Assert.Null(await Host.RunAsync<StaffAuthService, StaffSession?>(s => s.GetSessionAsync(ravi.MemberId, Token)));
        Assert.Equal("forbidden", (await Host.IssueAsync(ravi)).Error?.Code);
        await Host.RunAsync<StaffAuthService>(s => s.RequestSignInLinkAsync("ravi@acme.test", Token));
        Assert.Equal(emailsToRavi, Host.Email.To("ravi@acme.test").Count);
    }

    [Fact]
    public async Task A_current_member_cannot_be_invited_twice_but_a_removed_one_can_return()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var ravi = await InviteAndAccept(admin, "ravi@acme.test");

        Assert.Equal("team.already_member", (await Invite(admin, "ravi@acme.test")).Error?.Code);

        await Host.RunAsync<TeamService, Result>(s => s.RemoveAsync(admin, ravi.MemberId, Token));
        var back = await InviteAndAccept(admin, "ravi@acme.test", MemberRole.Admin);
        Assert.Equal(ravi.MemberId, back.MemberId);
    }

    [Fact]
    public async Task The_team_list_shows_members_and_marks_you()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        await Invite(admin, "ravi@acme.test");

        var team = await Host.RunAsync<TeamService, Result<IReadOnlyList<TeamMemberView>>>(s => s.ListAsync(admin, Token));

        Assert.Collection(
            team.Value,
            me =>
            {
                Assert.Equal("priya@acme.test", me.Email.Value);
                Assert.True(me.IsYou);
                Assert.Equal(MemberStatus.Active, me.Status);
            },
            invited =>
            {
                Assert.Equal("ravi@acme.test", invited.Email.Value);
                Assert.False(invited.IsYou);
                Assert.Equal(MemberStatus.Invited, invited.Status);
            });
    }
}
