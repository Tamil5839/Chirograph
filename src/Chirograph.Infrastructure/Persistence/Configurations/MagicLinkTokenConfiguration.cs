using Chirograph.Domain.Access;
using Chirograph.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chirograph.Infrastructure.Persistence.Configurations;

internal sealed class MagicLinkTokenConfiguration : IEntityTypeConfiguration<MagicLinkToken>
{
    public void Configure(EntityTypeBuilder<MagicLinkToken> builder)
    {
        builder.ToTable("MagicLinkTokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(t => t.Email).IsRequired();

        // Two simultaneous clicks on the same link: the second save fails instead of signing in twice.
        builder.Property(t => t.ConsumedAt).IsConcurrencyToken();

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.ExpiresAt);

        builder.HasOne<Organization>().WithMany().HasForeignKey(t => t.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<OrganizationMember>().WithMany().HasForeignKey(t => t.MemberId).OnDelete(DeleteBehavior.Cascade);
    }
}
