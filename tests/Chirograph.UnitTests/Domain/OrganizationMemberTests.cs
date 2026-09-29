using Chirograph.Domain.Common;
using Chirograph.Domain.Organizations;
using Chirograph.UnitTests.Support;

namespace Chirograph.UnitTests.Domain;

public class OrganizationMemberTests
{
    [Fact]
    public void The_founder_is_an_admin_who_must_confirm_their_mailbox()
    {
        var organization = TestData.PendingOrganization();

        var founder = OrganizationMember.CreateFounder(organization, TestData.Email("priya@acme.test"), TestData.Now);

        Assert.Equal(MemberRole.Admin, founder.Role);
        Assert.Equal(MemberStatus.Invited, founder.Status);
        Assert.False(founder.IsActive);
    }

    [Fact]
    public void The_founder_must_use_an_address_on_the_domain()
    {
        var error = Assert.Throws<DomainException>(() => OrganizationMember.CreateFounder(
            TestData.PendingOrganization(), TestData.Email("priya@gmail.com"), TestData.Now));

        Assert.Equal("organization.email_not_on_domain", error.Code);
    }

    [Fact]
    public void Activation_is_idempotent()
    {
        var founder = OrganizationMember.CreateFounder(
            TestData.PendingOrganization(), TestData.Email("priya@acme.test"), TestData.Now);

        founder.Activate(TestData.Now);
        founder.Activate(TestData.Now.AddHours(1));

        Assert.True(founder.IsActive);
        Assert.Equal(TestData.Now, founder.ActivatedAt);
    }

    [Fact]
    public void An_admin_can_invite_a_teammate_on_the_domain()
    {
        var organization = TestData.VerifiedOrganization();
        var admin = TestData.ActiveAdmin(organization);

        var invitee = OrganizationMember.Invite(
            organization, admin, TestData.Email("ravi@acme.test"), MemberRole.Issuer, TestData.Now);

        Assert.Equal(MemberRole.Issuer, invitee.Role);
        Assert.Equal(MemberStatus.Invited, invitee.Status);
        Assert.Equal(organization.Id, invitee.OrganizationId);
    }

    [Fact]
    public void Teammates_can_only_be_invited_once_the_domain_is_verified()
    {
        var organization = TestData.PendingOrganization();
        var admin = TestData.ActiveAdmin(organization);

        var error = Assert.Throws<DomainException>(() => OrganizationMember.Invite(
            organization, admin, TestData.Email("ravi@acme.test"), MemberRole.Issuer, TestData.Now));

        Assert.Equal("team.organization_not_verified", error.Code);
    }

    [Fact]
    public void Invitations_must_be_for_an_address_on_the_verified_domain()
    {
        var organization = TestData.VerifiedOrganization();
        var admin = TestData.ActiveAdmin(organization);

        var error = Assert.Throws<DomainException>(() => OrganizationMember.Invite(
            organization, admin, TestData.Email("ravi@gmail.com"), MemberRole.Issuer, TestData.Now));

        Assert.Equal("organization.email_not_on_domain", error.Code);
    }

    [Fact]
    public void Issuers_cannot_invite()
    {
        var organization = TestData.VerifiedOrganization();
        var issuer = TestData.ActiveIssuer(organization, TestData.ActiveAdmin(organization));

        var error = Assert.Throws<DomainException>(() => OrganizationMember.Invite(
            organization, issuer, TestData.Email("meera@acme.test"), MemberRole.Issuer, TestData.Now));

        Assert.Equal("team.admin_required", error.Code);
    }

    [Fact]
    public void Admins_of_another_organization_cannot_invite()
    {
        var acme = TestData.VerifiedOrganization();
        var outsider = TestData.ActiveAdmin(TestData.VerifiedOrganization("globex.test"), "hank");

        var error = Assert.Throws<DomainException>(() => OrganizationMember.Invite(
            acme, outsider, TestData.Email("meera@acme.test"), MemberRole.Admin, TestData.Now));

        Assert.Equal("member.wrong_organization", error.Code);
    }

    [Fact]
    public void A_removed_member_can_no_longer_act_and_cannot_reactivate()
    {
        var organization = TestData.VerifiedOrganization();
        var admin = TestData.ActiveAdmin(organization);
        var issuer = TestData.ActiveIssuer(organization, admin);

        issuer.Remove(organization, admin, TestData.Now);

        Assert.Equal(MemberStatus.Removed, issuer.Status);
        Assert.Equal("member.inactive", Assert.Throws<DomainException>(() => issuer.EnsureCanActFor(organization.Id)).Code);
        Assert.Equal("member.removed", Assert.Throws<DomainException>(() => issuer.Activate(TestData.Now)).Code);
    }

    [Fact]
    public void Admins_cannot_remove_themselves()
    {
        var organization = TestData.VerifiedOrganization();
        var admin = TestData.ActiveAdmin(organization);

        var error = Assert.Throws<DomainException>(() => admin.Remove(organization, admin, TestData.Now));

        Assert.Equal("team.cannot_remove_self", error.Code);
    }

    [Fact]
    public void A_removed_member_can_be_invited_again()
    {
        var organization = TestData.VerifiedOrganization();
        var admin = TestData.ActiveAdmin(organization);
        var issuer = TestData.ActiveIssuer(organization, admin);
        issuer.Remove(organization, admin, TestData.Now);

        issuer.Reinvite(organization, admin, MemberRole.Admin, TestData.Now.AddDays(1));
        issuer.Activate(TestData.Now.AddDays(1));

        Assert.True(issuer.IsActiveAdmin);
        Assert.Null(issuer.RemovedAt);
    }

    [Fact]
    public void An_existing_member_cannot_be_invited_again()
    {
        var organization = TestData.VerifiedOrganization();
        var admin = TestData.ActiveAdmin(organization);
        var issuer = TestData.ActiveIssuer(organization, admin);

        var error = Assert.Throws<DomainException>(
            () => issuer.Reinvite(organization, admin, MemberRole.Issuer, TestData.Now));

        Assert.Equal("team.already_member", error.Code);
    }
}
