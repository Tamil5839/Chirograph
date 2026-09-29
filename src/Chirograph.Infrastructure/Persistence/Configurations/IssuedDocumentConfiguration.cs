using Chirograph.Domain.Documents;
using Chirograph.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chirograph.Infrastructure.Persistence.Configurations;

internal sealed class IssuedDocumentConfiguration : IEntityTypeConfiguration<IssuedDocument>
{
    public void Configure(EntityTypeBuilder<IssuedDocument> builder)
    {
        builder.ToTable("Documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.VerificationId).IsRequired();
        builder.Property(d => d.EmployeeName).HasMaxLength(DocumentDetails.EmployeeNameMaxLength).IsRequired();
        builder.Property(d => d.EmployeeEmail).IsRequired();
        builder.Property(d => d.Designation).HasMaxLength(DocumentDetails.DesignationMaxLength).IsRequired();
        builder.Property(d => d.Fingerprint).HasColumnName("Sha256").IsRequired();
        builder.Property(d => d.FileKey).HasMaxLength(IssuedDocument.FileKeyMaxLength).IsRequired();
        builder.Property(d => d.RevocationReason).HasMaxLength(IssuedDocument.RevocationReasonMaxLength);

        builder.HasIndex(d => d.VerificationId).IsUnique();
        builder.HasIndex(d => d.EmployeeEmail);
        builder.HasIndex(d => new { d.OrganizationId, d.IssuedAt });

        builder.HasOne<Organization>().WithMany().HasForeignKey(d => d.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrganizationMember>().WithMany().HasForeignKey(d => d.IssuedByMemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrganizationMember>().WithMany().HasForeignKey(d => d.RevokedByMemberId).OnDelete(DeleteBehavior.Restrict);
    }
}
