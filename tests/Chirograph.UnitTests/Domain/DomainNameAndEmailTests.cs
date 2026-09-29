using Chirograph.Domain.Common;

namespace Chirograph.UnitTests.Domain;

public class DomainNameAndEmailTests
{
    [Theory]
    [InlineData("acme.com", "acme.com")]
    [InlineData("  ACME.Com. ", "acme.com")]
    [InlineData("hr.acme.co.in", "hr.acme.co.in")]
    public void Domain_names_are_normalised_to_lower_case_ascii(string input, string expected)
    {
        Assert.Equal(expected, DomainName.Parse(input).Value);
    }

    [Fact]
    public void Internationalised_domain_names_are_stored_as_punycode()
    {
        var domain = DomainName.Parse("Bücher.de");

        Assert.Equal("xn--bcher-kva.de", domain.Value);
        Assert.True(domain.IsInternationalized);
        Assert.Equal("bücher.de", domain.ToUnicode());
    }

    [Fact]
    public void A_look_alike_domain_using_another_script_is_visibly_different()
    {
        var genuine = DomainName.Parse("acme.com");
        var lookAlike = DomainName.Parse("аcme.com"); // Cyrillic 'а' instead of Latin 'a'

        Assert.NotEqual(genuine, lookAlike);
        Assert.StartsWith("xn--", lookAlike.Value, StringComparison.Ordinal);
        Assert.False(genuine.IsInternationalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("https://acme.com")]
    [InlineData("acme.com/careers")]
    [InlineData("hr@acme.com")]
    [InlineData("acme..com")]
    [InlineData("-acme.com")]
    [InlineData("acme-.com")]
    [InlineData("acme_hr.com")]
    [InlineData("192.168.0.1")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.com")]
    public void Invalid_domain_names_are_rejected(string input)
    {
        Assert.False(DomainName.TryParse(input, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("gmail.com")]
    [InlineData("outlook.com")]
    [InlineData("rediffmail.com")]
    public void Public_mailbox_providers_are_recognised(string domain)
    {
        Assert.True(FreeEmailProviders.Contains(DomainName.Parse(domain)));
    }

    [Fact]
    public void Email_addresses_are_normalised()
    {
        var email = EmailAddress.Parse("  Priya.Sharma+HR@ACME.com ");

        Assert.Equal("priya.sharma+hr@acme.com", email.Value);
        Assert.Equal("priya.sharma+hr", email.LocalPart);
        Assert.Equal(DomainName.Parse("acme.com"), email.Domain);
    }

    [Fact]
    public void An_address_is_on_a_domain_only_for_an_exact_match()
    {
        var acme = DomainName.Parse("acme.com");

        Assert.True(EmailAddress.Parse("priya@acme.com").IsOn(acme));
        Assert.False(EmailAddress.Parse("priya@hr.acme.com").IsOn(acme));
        Assert.False(EmailAddress.Parse("priya@acme.com.evil.test").IsOn(acme));
        Assert.False(EmailAddress.Parse("priya@notacme.com").IsOn(acme));
    }

    [Theory]
    [InlineData("")]
    [InlineData("priya")]
    [InlineData("@acme.com")]
    [InlineData("priya@")]
    [InlineData("pri ya@acme.com")]
    [InlineData("priya@@acme.com")]
    [InlineData("priya..sharma@acme.com")]
    [InlineData(".priya@acme.com")]
    [InlineData("priya@acme")]
    [InlineData("\"priya\"@acme.com")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa@acme.com")]
    public void Invalid_email_addresses_are_rejected(string input)
    {
        Assert.False(EmailAddress.TryParse(input, out _, out _));
        Assert.Throws<DomainException>(() => EmailAddress.Parse(input));
    }
}
