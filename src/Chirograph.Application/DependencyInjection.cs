using Chirograph.Application.Access;
using Chirograph.Application.Common;
using Chirograph.Application.Documents;
using Chirograph.Application.Employees;
using Chirograph.Application.Organizations;
using Chirograph.Application.Verification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chirograph.Application;

public static class DependencyInjection
{
    /// <summary>Registers the use cases. Configure <see cref="ChirographOptions"/> separately (the web host binds it).</summary>
    public static IServiceCollection AddChirographApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<LinkBuilder>();
        services.AddScoped<MagicLinkService>();
        services.AddScoped<StaffAuthService>();
        services.AddScoped<OrganizationSignupService>();
        services.AddScoped<DomainVerificationService>();
        services.AddScoped<TeamService>();
        services.AddScoped<DocumentIssuanceService>();
        services.AddScoped<IssuerDocumentsService>();
        services.AddScoped<VerificationService>();
        services.AddScoped<EmployeePortalService>();
        return services;
    }
}
