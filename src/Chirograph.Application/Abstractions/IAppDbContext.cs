using Chirograph.Domain.Access;
using Chirograph.Domain.Documents;
using Chirograph.Domain.Organizations;
using Chirograph.Domain.Verification;
using Microsoft.EntityFrameworkCore;

namespace Chirograph.Application.Abstractions;

/// <summary>Unit of work over Chirograph's aggregates. Provider-agnostic; implemented in Infrastructure.</summary>
public interface IAppDbContext
{
    DbSet<Organization> Organizations { get; }

    DbSet<OrganizationMember> OrganizationMembers { get; }

    DbSet<IssuedDocument> Documents { get; }

    DbSet<VerificationEvent> VerificationEvents { get; }

    DbSet<MagicLinkToken> MagicLinkTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>True when <paramref name="exception"/> was caused by a unique index (e.g. a domain already verified).</summary>
    bool IsUniqueConstraintViolation(DbUpdateException exception);
}
