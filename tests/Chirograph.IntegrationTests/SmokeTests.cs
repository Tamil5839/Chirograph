using Microsoft.AspNetCore.Mvc.Testing;

namespace Chirograph.IntegrationTests;

public class SmokeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Home_page_responds()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
