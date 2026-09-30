using System.Net;
using Chirograph.IntegrationTests.Support;

namespace Chirograph.IntegrationTests;

public sealed class FileCheckLimitFactory : ChirographWebFactory
{
    protected override IDictionary<string, string> Settings
    {
        get
        {
            var settings = base.Settings;
            settings["RateLimits:FileChecksPerTenMinutes"] = "3";
            return settings;
        }
    }
}

public sealed class ViewLimitFactory : ChirographWebFactory
{
    protected override IDictionary<string, string> Settings
    {
        get
        {
            var settings = base.Settings;
            settings["RateLimits:VerificationViewsPerMinute"] = "5";
            return settings;
        }
    }
}

public sealed class EmailRequestLimitFactory : ChirographWebFactory
{
    protected override IDictionary<string, string> Settings
    {
        get
        {
            var settings = base.Settings;
            settings["RateLimits:EmailRequestsPerFifteenMinutes"] = "2";
            return settings;
        }
    }
}

public sealed class FileCheckRateLimitTests(FileCheckLimitFactory factory) : IClassFixture<FileCheckLimitFactory>
{
    [Fact]
    public async Task File_checks_beyond_the_limit_are_refused()
    {
        var (_, issued) = await factory.SeedIssuedDocumentAsync();
        using var verifier = factory.CreateBrowser();
        var page = await verifier.GetAsync($"/v/{issued.VerificationId}");
        var file = new Dictionary<string, (string, byte[])> { ["Upload"] = ("letter.pdf", issued.StampedPdf) };

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await verifier.SubmitAsync(page, "#check-form", files: file)).StatusCode);
        var refused = await verifier.SubmitAsync(page, "#check-form", files: file);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Contains("Too many requests", refused.Text, StringComparison.Ordinal);
    }
}

public sealed class ViewRateLimitTests(ViewLimitFactory factory) : IClassFixture<ViewLimitFactory>
{
    [Fact]
    public async Task Verification_lookups_beyond_the_limit_are_refused()
    {
        using var visitor = factory.CreateBrowser();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 6; i++)
            statuses.Add((await visitor.GetRawAsync($"/v/{Chirograph.Domain.Documents.VerificationId.New()}")).StatusCode);

        Assert.All(statuses.Take(5), status => Assert.Equal(HttpStatusCode.NotFound, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[5]);
    }
}

public sealed class EmailRequestRateLimitTests(EmailRequestLimitFactory factory) : IClassFixture<EmailRequestLimitFactory>
{
    [Fact]
    public async Task Requests_that_send_email_are_limited_but_viewing_the_form_is_not()
    {
        using var visitor = factory.CreateBrowser();
        var form = await visitor.GetAsync("/auth/signin");
        var values = new Dictionary<string, string> { ["Email"] = "someone@example.org" };

        var first = await visitor.SubmitAsync(form, "#signin-form", values);
        var second = await visitor.SubmitAsync(form, "#signin-form", values);
        var third = await visitor.SubmitAsync(form, "#signin-form", values);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/auth/signin")).StatusCode);
    }
}
