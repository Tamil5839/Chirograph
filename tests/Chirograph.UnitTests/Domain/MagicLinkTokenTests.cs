using Chirograph.Domain.Access;
using Chirograph.Domain.Common;
using Chirograph.UnitTests.Support;

namespace Chirograph.UnitTests.Domain;

public class MagicLinkTokenTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    private static (MagicLinkToken Token, string Raw) CreateSignIn(TimeSpan? lifetime = null) =>
        MagicLinkToken.Create(
            MagicLinkPurpose.StaffSignIn,
            TestData.Email("priya@acme.test"),
            OrganizationId,
            MemberId,
            lifetime ?? TimeSpan.FromMinutes(20),
            TestData.Now);

    [Fact]
    public void Only_a_hash_of_the_token_is_kept()
    {
        var (token, raw) = CreateSignIn();

        Assert.Equal(43, raw.Length); // 32 random bytes, base64url without padding
        Assert.NotEqual(raw, token.TokenHash);
        Assert.DoesNotContain(raw, token.TokenHash, StringComparison.Ordinal);
        Assert.Equal(MagicLinkToken.Hash(raw), token.TokenHash);
        Assert.Matches("^[0-9a-f]{64}$", token.TokenHash);
    }

    [Fact]
    public void Every_token_is_different()
    {
        var raws = Enumerable.Range(0, 1000).Select(_ => CreateSignIn().Raw).ToHashSet();

        Assert.Equal(1000, raws.Count);
    }

    [Fact]
    public void A_token_can_be_used_once()
    {
        var (token, _) = CreateSignIn();

        token.Consume(TestData.Now.AddMinutes(1));

        Assert.False(token.IsUsable(TestData.Now.AddMinutes(1)));
        Assert.Equal("link.used", Assert.Throws<DomainException>(() => token.Consume(TestData.Now.AddMinutes(2))).Code);
    }

    [Fact]
    public void A_token_expires()
    {
        var (token, _) = CreateSignIn(TimeSpan.FromMinutes(20));

        Assert.True(token.IsUsable(TestData.Now.AddMinutes(19)));
        Assert.False(token.IsUsable(TestData.Now.AddMinutes(20)));
        Assert.Equal("link.expired", Assert.Throws<DomainException>(() => token.Consume(TestData.Now.AddMinutes(20))).Code);
    }

    [Theory]
    [InlineData(MagicLinkPurpose.StaffSignIn)]
    [InlineData(MagicLinkPurpose.StaffInvite)]
    [InlineData(MagicLinkPurpose.DomainVerification)]
    public void Staff_links_must_identify_the_member(MagicLinkPurpose purpose)
    {
        Assert.Throws<ArgumentException>(() => MagicLinkToken.Create(
            purpose, TestData.Email("priya@acme.test"), OrganizationId, null, TimeSpan.FromMinutes(5), TestData.Now));
    }

    [Fact]
    public void Employee_links_are_tied_to_an_email_address_only()
    {
        var (token, _) = MagicLinkToken.Create(
            MagicLinkPurpose.EmployeeSignIn, TestData.Email("anita@example.org"), null, null, TimeSpan.FromHours(1), TestData.Now);

        Assert.Null(token.OrganizationId);
        Assert.Throws<ArgumentException>(() => MagicLinkToken.Create(
            MagicLinkPurpose.EmployeeSignIn, TestData.Email("anita@example.org"), OrganizationId, null, TimeSpan.FromHours(1), TestData.Now));
    }
}
