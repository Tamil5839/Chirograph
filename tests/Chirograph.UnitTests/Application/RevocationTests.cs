using Chirograph.Application.Common;
using Chirograph.Application.Documents;
using Chirograph.Application.Verification;
using Chirograph.Domain.Documents;
using Chirograph.Domain.Verification;
using Chirograph.UnitTests.Support;

namespace Chirograph.UnitTests.Application;

public class RevocationTests : ApplicationTestBase
{
    private Task<Result> Revoke(StaffActor actor, Guid documentId, string reason) =>
        Host.RunAsync<IssuerDocumentsService, Result>(s => s.RevokeAsync(actor, documentId, reason, Token));

    [Fact]
    public async Task Verifiers_see_a_revoked_document_as_revoked_with_the_date()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var issued = await Host.IssueSampleAsync(admin);
        Host.Time.Advance(TimeSpan.FromDays(2));

        Assert.True((await Revoke(admin, issued.DocumentId, "Issued with the wrong end date")).Succeeded);

        var view = await Host.RunAsync<VerificationService, VerificationResult>(s => s.ViewAsync(issued.VerificationId.Value, Token));
        Assert.Equal(VerificationVerdict.Revoked, view.Outcome.Verdict);
        Assert.Equal(TestData.Now.AddDays(2), view.Outcome.RevokedAt);
        Assert.Equal(DocumentStatus.Revoked, view.Document?.Status);
    }

    [Fact]
    public async Task A_revoked_document_fails_even_when_the_exact_file_is_presented()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var issued = await Host.IssueSampleAsync(admin);
        await Revoke(admin, issued.DocumentId, "Issued in error");

        var result = await Host.RunAsync<VerificationService, VerificationResult>(
            s => s.CheckFileAsync(issued.VerificationId.Value, new MemoryStream(issued.StampedPdf), null, Token));

        Assert.Equal(VerificationVerdict.Revoked, result.Outcome.Verdict);
        Assert.Equal(FileCheckResult.Match, result.Outcome.FileCheck);
        Assert.True(result.Outcome.Failed);
    }

    [Fact]
    public async Task The_employee_is_told_the_document_was_revoked_and_why()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var issued = await Host.IssueSampleAsync(admin);

        await Revoke(admin, issued.DocumentId, "Issued with the wrong end date");

        var notice = Host.Email.To("anita.desai@example.org").Last();
        Assert.Contains("revoked", notice.Subject, StringComparison.Ordinal);
        Assert.Contains("Issued with the wrong end date", notice.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_issuer_sees_who_revoked_it_and_why()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var issued = await Host.IssueSampleAsync(admin);
        await Revoke(admin, issued.DocumentId, "Issued in error");

        var view = await Host.RunAsync<IssuerDocumentsService, Result<IssuerDocumentView>>(s => s.GetAsync(admin, issued.DocumentId, Token));

        Assert.Equal(DocumentStatus.Revoked, view.Value.Status);
        Assert.Equal("Issued in error", view.Value.RevocationReason);
        Assert.Equal("priya@acme.test", view.Value.RevokedBy?.Value);
    }

    [Fact]
    public async Task Revoking_requires_a_reason()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var issued = await Host.IssueSampleAsync(admin);

        Assert.Equal("validation.required", (await Revoke(admin, issued.DocumentId, "  ")).Error?.Code);
    }

    [Fact]
    public async Task A_document_cannot_be_revoked_twice()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var issued = await Host.IssueSampleAsync(admin);
        await Revoke(admin, issued.DocumentId, "Issued in error");

        Assert.Equal("document.already_revoked", (await Revoke(admin, issued.DocumentId, "Again")).Error?.Code);
    }

    [Fact]
    public async Task Another_organization_can_neither_see_nor_revoke_the_document()
    {
        var acme = await Host.CreateVerifiedOrganizationAsync();
        var globex = await Host.CreateVerifiedOrganizationAsync("globex.test", "hank", "Globex Corporation");
        var issued = await Host.IssueSampleAsync(acme);

        Assert.Equal("not_found", (await Revoke(globex, issued.DocumentId, "Not ours")).Error?.Code);
        Assert.Equal("not_found", (await Host.RunAsync<IssuerDocumentsService, Result<IssuerDocumentView>>(s => s.GetAsync(globex, issued.DocumentId, Token))).Error?.Code);
        Assert.Equal("not_found", (await Host.RunAsync<IssuerDocumentsService, Result<StoredFile>>(s => s.OpenFileAsync(globex, issued.DocumentId, Token))).Error?.Code);
    }
}
