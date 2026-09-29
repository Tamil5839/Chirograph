using System.Reflection;

namespace Chirograph.UnitTests.Architecture;

/// <summary>
/// Enforces the clean-architecture dependency rule: Domain ← Application ← Infrastructure ← Web.
/// </summary>
public class LayeringTests
{
    private static readonly string[] InfrastructurePackages =
    [
        "Microsoft.EntityFrameworkCore.Sqlite",
        "Microsoft.EntityFrameworkCore.Relational",
        "Npgsql",
        "Npgsql.EntityFrameworkCore.PostgreSQL",
        "PdfSharp",
        "QRCoder",
        "DnsClient",
        "MailKit",
        "MimeKit",
    ];

    [Fact]
    public void Domain_depends_only_on_the_base_class_library()
    {
        var references = ReferencedAssemblyNames("Chirograph.Domain");

        var offenders = references
            .Where(name => name != "System" && !name.StartsWith("System.", StringComparison.Ordinal)
                                            && name is not ("netstandard" or "mscorlib"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_web()
    {
        var references = ReferencedAssemblyNames("Chirograph.Application");

        Assert.DoesNotContain("Chirograph.Infrastructure", references);
        Assert.DoesNotContain("Chirograph.Web", references);
        Assert.Empty(references.Intersect(InfrastructurePackages));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_web()
    {
        var references = ReferencedAssemblyNames("Chirograph.Infrastructure");

        Assert.DoesNotContain("Chirograph.Web", references);
    }

    private static HashSet<string> ReferencedAssemblyNames(string assemblyName) =>
        Assembly.Load(new AssemblyName(assemblyName))
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .ToHashSet(StringComparer.Ordinal);
}
