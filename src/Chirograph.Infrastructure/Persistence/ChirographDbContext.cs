using Chirograph.Application.Abstractions;
using Chirograph.Domain.Access;
using Chirograph.Domain.Common;
using Chirograph.Domain.Documents;
using Chirograph.Domain.Organizations;
using Chirograph.Domain.Verification;
using Microsoft.EntityFrameworkCore;

namespace Chirograph.Infrastructure.Persistence;

/// <summary>
/// Chirograph's EF Core model. Each database provider has a small subclass so that it can carry its own migration
/// history (see <see cref="SqliteChirographDbContext"/> and <see cref="PostgresChirographDbContext"/>).
/// </summary>
public abstract class ChirographDbContext(DbContextOptions options) : DbContext(options), IAppDbContext
{
    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();

    public DbSet<IssuedDocument> Documents => Set<IssuedDocument>();

    public DbSet<VerificationEvent> VerificationEvents => Set<VerificationEvent>();

    public DbSet<MagicLinkToken> MagicLinkTokens => Set<MagicLinkToken>();

    public abstract bool IsUniqueConstraintViolation(DbUpdateException exception);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ChirographDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DomainName>().HaveConversion<ValueConverters.DomainNameConverter>().HaveMaxLength(DomainName.MaxLength);
        configurationBuilder.Properties<EmailAddress>().HaveConversion<ValueConverters.EmailAddressConverter>().HaveMaxLength(EmailAddress.MaxLength);
        configurationBuilder.Properties<VerificationId>().HaveConversion<ValueConverters.VerificationIdConverter>().HaveMaxLength(VerificationId.Length);
        configurationBuilder.Properties<DocumentFingerprint>().HaveConversion<ValueConverters.DocumentFingerprintConverter>().HaveMaxLength(DocumentFingerprint.HexLength);

        // Enums are stored by name: readable in the database and safe against reordering.
        StoreAsString<OrganizationStatus>(configurationBuilder);
        StoreAsString<DomainVerificationMethod>(configurationBuilder);
        StoreAsString<MemberRole>(configurationBuilder);
        StoreAsString<MemberStatus>(configurationBuilder);
        StoreAsString<DocumentType>(configurationBuilder);
        StoreAsString<DocumentStatus>(configurationBuilder);
        StoreAsString<VerificationKind>(configurationBuilder);
        StoreAsString<MagicLinkPurpose>(configurationBuilder);
    }

    private static void StoreAsString<TEnum>(ModelConfigurationBuilder configurationBuilder)
        where TEnum : struct, Enum =>
        configurationBuilder.Properties<TEnum>().HaveConversion<string>().HaveMaxLength(32);
}
