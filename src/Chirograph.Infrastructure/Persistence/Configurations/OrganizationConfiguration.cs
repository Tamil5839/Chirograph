using Chirograph.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chirograph.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Name).HasMaxLength(Organization.NameMaxLength).IsRequired();
        builder.Property(o => o.Domain).IsRequired();
        builder.Property(o => o.DnsChallenge).HasMaxLength(64).IsRequired();
        builder.Property(o => o.VerificationEvidence).HasMaxLength(Organization.EvidenceMaxLength);

        builder.HasIndex(o => o.Domain, "IX_Organizations_Domain");

        // Anyone may start registering a domain, but only one organization can ever hold it verified. Verification
        // pages show this domain, so a second "Acme" can never appear under acme.com.
        builder.HasIndex(o => o.Domain, "UX_Organizations_VerifiedDomain")
            .IsUnique()
            .HasFilter($"\"{nameof(Organization.Status)}\" = '{nameof(OrganizationStatus.Verified)}'");
    }
}
