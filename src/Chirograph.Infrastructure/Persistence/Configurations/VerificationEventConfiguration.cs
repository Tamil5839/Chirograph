using Chirograph.Domain.Documents;
using Chirograph.Domain.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chirograph.Infrastructure.Persistence.Configurations;

internal sealed class VerificationEventConfiguration : IEntityTypeConfiguration<VerificationEvent>
{
    public void Configure(EntityTypeBuilder<VerificationEvent> builder)
    {
        builder.ToTable("VerificationEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.DeclaredVerifier).HasMaxLength(VerificationEvent.DeclaredVerifierMaxLength);

        builder.HasIndex(e => new { e.DocumentId, e.OccurredAt });

        builder.HasOne<IssuedDocument>().WithMany().HasForeignKey(e => e.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}
