using Chirograph.Application.Common;
using Chirograph.Domain.Documents;
using Microsoft.Extensions.Options;

namespace Chirograph.UnitTests.Application;

public class LinkBuilderTests
{
    private static readonly VerificationId Id = VerificationId.Parse("7K3M9QXAB2C4DEFGH8JKMNP2Q");

    private static LinkBuilder For(string baseUrl) =>
        new(Options.Create(new ChirographOptions { PublicBaseUrl = new Uri(baseUrl) }));

    [Fact]
    public void Links_are_built_from_the_configured_public_address()
    {
        var links = For("https://chirograph.example");

        Assert.Equal("https://chirograph.example/v/7K3M9QXAB2C4DEFGH8JKMNP2Q", links.Verification(Id).AbsoluteUri);
        Assert.Equal("https://chirograph.example/v", links.VerifyPage.AbsoluteUri);
        Assert.Equal("https://chirograph.example/me", links.EmployeePortal.AbsoluteUri);
        Assert.Equal("https://chirograph.example/auth/link?token=a%2Bb", links.MagicLink("a+b").AbsoluteUri);
    }

    [Fact]
    public void A_path_base_in_the_public_address_is_kept()
    {
        var links = For("https://intranet.example.org/chirograph/");

        Assert.Equal("https://intranet.example.org/chirograph/v/7K3M9QXAB2C4DEFGH8JKMNP2Q", links.Verification(Id).AbsoluteUri);
    }
}
