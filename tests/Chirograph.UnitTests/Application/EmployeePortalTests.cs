using Chirograph.Application.Common;
using Chirograph.Application.Documents;
using Chirograph.Application.Employees;
using Chirograph.Application.Verification;
using Chirograph.Domain.Common;
using Chirograph.Domain.Documents;
using Chirograph.UnitTests.Support;

namespace Chirograph.UnitTests.Application;

public class EmployeePortalTests : ApplicationTestBase
{
    private async Task<EmployeeSession> SignInFromLatestEmail(string email)
    {
        var session = await Host.RunAsync<EmployeePortalService, Result<EmployeeSession>>(
            s => s.CompleteSignInAsync(ApplicationTestHost.LinkToken(Host.Email.To(email).Last()), Token));
        Assert.True(session.Succeeded, session.Error?.Message);
        return session.Value;
    }

    [Fact]
    public async Task The_issue_email_lets_the_employee_see_their_document_and_who_verified_it()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var issued = await Host.IssueSampleAsync(admin);
        await Host.RunAsync<VerificationService, VerificationResult>(
            s => s.CheckFileAsync(issued.VerificationId.Value, new MemoryStream(issued.StampedPdf), "Globex HR", Token));

        var employee = await SignInFromLatestEmail("anita.desai@example.org");

        var summary = Assert.Single(await Host.RunAsync<EmployeePortalService, IReadOnlyList<EmployeeDocumentSummary>>(s => s.ListDocumentsAsync(employee, Token)));
        Assert.Equal("Acme Technologies", summary.IssuerName);
        Assert.Equal(1, summary.VerificationCount);
        var document = await Host.RunAsync<EmployeePortalService, Result<EmployeeDocumentView>>(s => s.GetDocumentAsync(employee, issued.DocumentId, Token));
        var entry = Assert.Single(document.Value.Verifications);
        Assert.Equal("Globex HR", entry.DeclaredVerifier);
        Assert.True(entry.FileMatched);
    }

    [Fact]
    public async Task An_employee_can_ask_for_a_fresh_link()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        await Host.IssueSampleAsync(admin);

        await Host.RunAsync<EmployeePortalService>(s => s.RequestSignInLinkAsync("Anita.Desai@example.org", Token));

        Assert.Equal("Your Chirograph documents sign-in link", Host.Email.Last.Subject);
        Assert.Equal("anita.desai@example.org", (await SignInFromLatestEmail("anita.desai@example.org")).Email.Value);
    }

    [Fact]
    public async Task Addresses_without_documents_are_sent_nothing()
    {
        await Host.RunAsync<EmployeePortalService>(s => s.RequestSignInLinkAsync("nobody@example.org", Token));

        Assert.Empty(Host.Email.Sent);
    }

    [Fact]
    public async Task Employees_see_only_their_own_documents()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        await Host.IssueSampleAsync(admin, "anita.desai@example.org");
        var other = await Host.IssueSampleAsync(admin, "bala.k@example.org");
        var anita = new EmployeeSession(EmailAddress.Parse("anita.desai@example.org"));

        var documents = await Host.RunAsync<EmployeePortalService, IReadOnlyList<EmployeeDocumentSummary>>(s => s.ListDocumentsAsync(anita, Token));

        Assert.Single(documents);
        Assert.Equal("not_found", (await Host.RunAsync<EmployeePortalService, Result<EmployeeDocumentView>>(s => s.GetDocumentAsync(anita, other.DocumentId, Token))).Error?.Code);
        Assert.Equal("not_found", (await Host.RunAsync<EmployeePortalService, Result<StoredFile>>(s => s.OpenFileAsync(anita, other.DocumentId, Token))).Error?.Code);
    }

    [Fact]
    public async Task Letters_from_different_employers_appear_together()
    {
        var acme = await Host.CreateVerifiedOrganizationAsync();
        var globex = await Host.CreateVerifiedOrganizationAsync("globex.test", "hank", "Globex Corporation");
        await Host.IssueSampleAsync(acme);
        Host.Time.Advance(TimeSpan.FromDays(400));
        await Host.IssueSampleAsync(globex);
        var anita = new EmployeeSession(EmailAddress.Parse("anita.desai@example.org"));

        var documents = await Host.RunAsync<EmployeePortalService, IReadOnlyList<EmployeeDocumentSummary>>(s => s.ListDocumentsAsync(anita, Token));

        Assert.Equal(["Globex Corporation", "Acme Technologies"], documents.Select(d => d.IssuerName));
    }

    [Fact]
    public async Task The_employee_sees_why_a_document_was_revoked()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var issued = await Host.IssueSampleAsync(admin);
        await Host.RunAsync<IssuerDocumentsService, Result>(s => s.RevokeAsync(admin, issued.DocumentId, "Issued with the wrong end date", Token));
        var anita = new EmployeeSession(EmailAddress.Parse("anita.desai@example.org"));

        var document = await Host.RunAsync<EmployeePortalService, Result<EmployeeDocumentView>>(s => s.GetDocumentAsync(anita, issued.DocumentId, Token));

        Assert.Equal(DocumentStatus.Revoked, document.Value.Status);
        Assert.Equal("Issued with the wrong end date", document.Value.RevocationReason);
    }

    [Fact]
    public async Task The_employee_can_download_exactly_the_issued_file()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var issued = await Host.IssueSampleAsync(admin);
        var anita = new EmployeeSession(EmailAddress.Parse("anita.desai@example.org"));

        var file = await Host.RunAsync<EmployeePortalService, Result<StoredFile>>(s => s.OpenFileAsync(anita, issued.DocumentId, Token));

        await using var content = file.Value.Content;
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, Token);
        Assert.Equal(issued.StampedPdf, copy.ToArray());
        Assert.Equal(issued.FileName, file.Value.FileName);
    }
}
