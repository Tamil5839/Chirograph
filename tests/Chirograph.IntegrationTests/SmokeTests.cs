using Chirograph.IntegrationTests.Support;

namespace Chirograph.IntegrationTests;

public sealed class SmokeTests(ChirographWebFactory factory) : IClassFixture<ChirographWebFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/v")]
    [InlineData("/signup")]
    [InlineData("/auth/signin")]
    [InlineData("/me")]
    [InlineData("/privacy")]
    public async Task Public_pages_respond(string path)
    {
        using var browser = factory.CreateBrowser();

        var page = await browser.GetAsync(path);

        page.Response.EnsureSuccessStatusCode();
    }
}
