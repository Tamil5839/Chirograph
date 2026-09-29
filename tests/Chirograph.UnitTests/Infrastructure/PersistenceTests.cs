using Chirograph.Domain.Access;
using Chirograph.Domain.Documents;
using Chirograph.Domain.Organizations;
using Chirograph.Domain.Verification;
using Chirograph.Infrastructure.Persistence;
using Chirograph.UnitTests.Support;
using Microsoft.EntityFrameworkCore;

namespace Chirograph.UnitTests.Infrastructure;

public class MigrationTests
{
    [Fact]
    public async Task The_checked_in_migrations_match_the_model_for_both_providers()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var sqlite = database.CreateContext();
        await using var postgres = new PostgresChirographDbContext(
            new DbContextOptionsBuilder<PostgresChirographDbContext>().UseNpgsql("Host=localhost;Database=unused").Options);

        Assert.False(sqlite.Database.HasPendingModelChanges(), "Run `dotnet ef migrations add` for SqliteChirographDbContext.");
        Assert.False(postgres.Database.HasPendingModelChanges(), "Run `dotnet ef migrations add` for PostgresChirographDbContext.");
        Assert.Empty(await sqlite.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }
}

public sealed class SqlitePersistenceTests : PersistenceContractTests
{
    private protected override async Task<ITestDatabase> CreateDatabaseAsync() => await SqliteTestDatabase.CreateAsync();
}

/// <summary>
/// Runs the same contract against a real PostgreSQL server when <c>CHIROGRAPH_TEST_POSTGRES</c> holds a connection
/// string (the role needs CREATEDB); each test gets a fresh database. Skipped otherwise.
/// </summary>
public sealed class PostgresPersistenceTests : PersistenceContractTests
{
    private protected override async Task<ITestDatabase> CreateDatabaseAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgresTestDatabase.Variable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(connectionString), $"Set {PostgresTestDatabase.Variable} to run against PostgreSQL.");
        return await PostgresTestDatabase.CreateAsync(connectionString!);
    }
}

/// <summary>Persistence behaviour every supported database provider must honour.</summary>
public abstract class PersistenceContractTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private protected abstract Task<ITestDatabase> CreateDatabaseAsync();

    [Fact]
    public async Task Every_entity_and_value_object_round_trips()
    {
        await using var database = await CreateDatabaseAsync();
        var precise = TestData.Now.AddTicks(1_234_560); // databases keep microseconds, the finest precision stored
        var organization = TestData.PendingOrganization();
        organization.MarkVerified(DomainVerificationMethod.AdminMailbox, "Confirmed by admin@acme.test", precise);
        var admin = TestData.ActiveAdmin(organization);
        var issuer = TestData.ActiveIssuer(organization, admin);
        var document = TestData.IssuedDocument(organization, issuer);
        document.Revoke(admin, "Issued with the wrong end date", precise.AddDays(1));
        var view = VerificationEvent.Record(document, VerificationOutcome.Evaluate(document, document.Fingerprint), "Globex HR", precise);
        var (token, _) = MagicLinkToken.Create(MagicLinkPurpose.StaffSignIn, admin.Email, organization.Id, admin.Id, TimeSpan.FromMinutes(20), precise);

        await using (var context = database.CreateContext())
        {
            context.AddRange(organization, admin, issuer, document, view, token);
            await context.SaveChangesAsync(Token);
        }

        await using var reader = database.CreateContext();
        var storedOrganization = await reader.Organizations.SingleAsync(Token);
        Assert.Equal(organization.Domain, storedOrganization.Domain);
        Assert.Equal(OrganizationStatus.Verified, storedOrganization.Status);
        Assert.Equal(DomainVerificationMethod.AdminMailbox, storedOrganization.VerificationMethod);
        Assert.Equal(precise, storedOrganization.VerifiedAt);
        Assert.Equal(organization.DnsChallenge, storedOrganization.DnsChallenge);

        var storedIssuer = await reader.OrganizationMembers.SingleAsync(m => m.Id == issuer.Id, Token);
        Assert.Equal(issuer.Email, storedIssuer.Email);
        Assert.Equal(MemberRole.Issuer, storedIssuer.Role);
        Assert.Equal(MemberStatus.Active, storedIssuer.Status);

        var storedDocument = await reader.Documents.SingleAsync(Token);
        Assert.Equal(document.VerificationId, storedDocument.VerificationId);
        Assert.Equal(document.Fingerprint, storedDocument.Fingerprint);
        Assert.Equal(document.EmployeeEmail, storedDocument.EmployeeEmail);
        Assert.Equal(document.EmploymentStart, storedDocument.EmploymentStart);
        Assert.Equal(document.EmploymentEnd, storedDocument.EmploymentEnd);
        Assert.Equal(DocumentType.ExperienceLetter, storedDocument.Type);
        Assert.Equal(DocumentStatus.Revoked, storedDocument.Status);
        Assert.Equal(precise.AddDays(1), storedDocument.RevokedAt);
        Assert.Equal("Issued with the wrong end date", storedDocument.RevocationReason);

        var storedEvent = await reader.VerificationEvents.SingleAsync(Token);
        Assert.Equal(VerificationKind.FileChecked, storedEvent.Kind);
        Assert.Equal(DocumentStatus.Revoked, storedEvent.StatusShown);
        Assert.True(storedEvent.FileMatched);
        Assert.Equal("Globex HR", storedEvent.DeclaredVerifier);

        var storedToken = await reader.MagicLinkTokens.SingleAsync(Token);
        Assert.Equal(token.TokenHash, storedToken.TokenHash);
        Assert.Equal(precise.AddMinutes(20), storedToken.ExpiresAt);
    }

    [Fact]
    public async Task Several_pending_organizations_may_claim_a_domain_but_only_one_can_hold_it_verified()
    {
        await using var database = await CreateDatabaseAsync();
        var genuine = TestData.PendingOrganization(name: "Acme Technologies");
        var impostor = TestData.PendingOrganization(name: "Acme Tech (HR)");
        await using (var context = database.CreateContext())
        {
            context.AddRange(genuine, impostor);
            await context.SaveChangesAsync(Token);
        }

        await using (var context = database.CreateContext())
        {
            var stored = await context.Organizations.SingleAsync(o => o.Id == genuine.Id, Token);
            stored.MarkVerified(DomainVerificationMethod.DnsTxtRecord, "TXT record", TestData.Now);
            await context.SaveChangesAsync(Token);
        }

        await using var late = database.CreateContext();
        var second = await late.Organizations.SingleAsync(o => o.Id == impostor.Id, Token);
        second.MarkVerified(DomainVerificationMethod.DnsTxtRecord, "TXT record", TestData.Now);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => late.SaveChangesAsync(Token));
        Assert.True(late.IsUniqueConstraintViolation(error));
    }

    [Fact]
    public async Task Verification_ids_are_unique()
    {
        await using var database = await CreateDatabaseAsync();
        var organization = TestData.VerifiedOrganization();
        var admin = TestData.ActiveAdmin(organization);
        var id = VerificationId.New();
        IssuedDocument Issue() => IssuedDocument.Issue(
            organization, admin, TestData.Details(), id, DocumentFingerprint.Compute([1, 2, 3]), 3, $"documents/{Guid.NewGuid():N}.pdf", TestData.Now);

        await using var context = database.CreateContext();
        context.AddRange(organization, admin, Issue(), Issue());

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
        Assert.True(context.IsUniqueConstraintViolation(error));
    }

    [Fact]
    public async Task A_person_can_belong_to_several_organizations_but_only_once_to_each()
    {
        await using var database = await CreateDatabaseAsync();
        var first = TestData.PendingOrganization(name: "Acme One");
        var second = TestData.PendingOrganization(name: "Acme Two");
        var email = TestData.Email("priya@acme.test");

        await using (var context = database.CreateContext())
        {
            context.AddRange(first, second,
                OrganizationMember.CreateFounder(first, email, TestData.Now),
                OrganizationMember.CreateFounder(second, email, TestData.Now));
            await context.SaveChangesAsync(Token);
        }

        await using var duplicate = database.CreateContext();
        duplicate.Add(OrganizationMember.CreateFounder(first, email, TestData.Now));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync(Token));
        Assert.True(duplicate.IsUniqueConstraintViolation(error));
    }

    [Fact]
    public async Task Timestamps_sort_and_compare_chronologically()
    {
        await using var database = await CreateDatabaseAsync();
        var organization = TestData.VerifiedOrganization();
        var admin = TestData.ActiveAdmin(organization);
        var document = TestData.IssuedDocument(organization, admin);
        var offsets = new[] { TimeSpan.FromDays(400), TimeSpan.FromMilliseconds(1), TimeSpan.FromMicroseconds(1), TimeSpan.Zero, TimeSpan.FromHours(30) };
        await using (var context = database.CreateContext())
        {
            context.AddRange(organization, admin, document);
            foreach (var offset in offsets)
                context.Add(VerificationEvent.Record(document, VerificationOutcome.Evaluate(document, null), null, TestData.Now + offset));
            await context.SaveChangesAsync(Token);
        }

        await using var reader = database.CreateContext();
        var ordered = await reader.VerificationEvents.OrderBy(e => e.OccurredAt).Select(e => e.OccurredAt).ToListAsync(Token);
        var afterCutoff = await reader.VerificationEvents.CountAsync(e => e.OccurredAt > TestData.Now, Token);

        Assert.Equal(offsets.Order().Select(offset => TestData.Now + offset), ordered);
        Assert.Equal(4, afterCutoff);
    }

    [Fact]
    public async Task A_magic_link_cannot_be_used_twice_even_by_simultaneous_requests()
    {
        await using var database = await CreateDatabaseAsync();
        var organization = TestData.PendingOrganization();
        var founder = OrganizationMember.CreateFounder(organization, TestData.Email("priya@acme.test"), TestData.Now);
        var (token, _) = MagicLinkToken.Create(MagicLinkPurpose.StaffSignIn, founder.Email, organization.Id, founder.Id, TimeSpan.FromMinutes(20), TestData.Now);
        await using (var context = database.CreateContext())
        {
            context.AddRange(organization, founder, token);
            await context.SaveChangesAsync(Token);
        }

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstCopy = await first.MagicLinkTokens.SingleAsync(Token);
        var secondCopy = await second.MagicLinkTokens.SingleAsync(Token);
        firstCopy.Consume(TestData.Now.AddMinutes(1));
        secondCopy.Consume(TestData.Now.AddMinutes(1));

        await first.SaveChangesAsync(Token);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Token));
    }
}
