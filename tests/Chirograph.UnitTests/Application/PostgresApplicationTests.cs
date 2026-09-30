using Chirograph.Application.Common;
using Chirograph.Application.Documents;
using Chirograph.Application.Employees;
using Chirograph.Application.Verification;
using Chirograph.Domain.Common;
using Chirograph.Domain.Documents;
using Chirograph.Domain.Verification;
using Chirograph.UnitTests.Support;

namespace Chirograph.UnitTests.Application;

/// <summary>
/// Runs the whole application flow, including every query shape the services use, against real PostgreSQL when
/// <c>CHIROGRAPH_TEST_POSTGRES</c> is set. Skipped otherwise.
/// </summary>
public class PostgresApplicationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_complete_flow_works_on_PostgreSQL()
    {
        var server = Environment.GetEnvironmentVariable(PostgresTestDatabase.Variable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(server), $"Set {PostgresTestDatabase.Variable} to run against PostgreSQL.");
        await using var host = await ApplicationTestHost.CreateAsync(postgresServer: server);

        var admin = await host.CreateVerifiedOrganizationAsync();
        var anita = await host.IssueSampleAsync(admin, "anita.desai@example.org");
        host.Time.Advance(TimeSpan.FromHours(1));
        var bala = await host.IssueSampleAsync(admin, "bala.k@example.org");

        var check = await host.RunAsync<VerificationService, VerificationResult>(
            s => s.CheckFileAsync(anita.VerificationId.ToDisplayString(), new MemoryStream(anita.StampedPdf), "Globex HR", Token));
        Assert.Equal(VerificationVerdict.Authentic, check.Outcome.Verdict);

        var all = await host.RunAsync<IssuerDocumentsService, Result<IReadOnlyList<DocumentSummary>>>(s => s.ListAsync(admin, null, Token));
        Assert.Equal([bala.DocumentId, anita.DocumentId], all.Value.Select(d => d.Id));
        Assert.Equal(1, all.Value.Single(d => d.Id == anita.DocumentId).VerificationCount);
        Assert.NotNull(all.Value.Single(d => d.Id == anita.DocumentId).LastVerifiedAt);

        var byName = await host.RunAsync<IssuerDocumentsService, Result<IReadOnlyList<DocumentSummary>>>(s => s.ListAsync(admin, "anita DESAI", Token));
        Assert.Equal(2, byName.Value.Count); // both samples are for "Anita Desai"; the email differs
        var byId = await host.RunAsync<IssuerDocumentsService, Result<IReadOnlyList<DocumentSummary>>>(s => s.ListAsync(admin, bala.VerificationId.ToDisplayString(), Token));
        Assert.Equal(bala.DocumentId, Assert.Single(byId.Value).Id);

        var recent = await host.RunAsync<IssuerDocumentsService, Result<IReadOnlyList<RecentVerification>>>(s => s.RecentVerificationsAsync(admin, 10, Token));
        Assert.Equal("Globex HR", Assert.Single(recent.Value).Entry.DeclaredVerifier);

        await host.RunAsync<IssuerDocumentsService, Result>(s => s.RevokeAsync(admin, bala.DocumentId, "Issued in error", Token));
        var employee = new EmployeeSession(EmailAddress.Parse("bala.k@example.org"));
        var documents = await host.RunAsync<EmployeePortalService, IReadOnlyList<EmployeeDocumentSummary>>(s => s.ListDocumentsAsync(employee, Token));
        Assert.Equal(DocumentStatus.Revoked, Assert.Single(documents).Status);
        var detail = await host.RunAsync<EmployeePortalService, Result<EmployeeDocumentView>>(s => s.GetDocumentAsync(employee, bala.DocumentId, Token));
        Assert.Equal("Issued in error", detail.Value.RevocationReason);
    }
}
