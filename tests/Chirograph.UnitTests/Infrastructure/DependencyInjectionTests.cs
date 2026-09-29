using Chirograph.Application.Abstractions;
using Chirograph.Infrastructure;
using Chirograph.Infrastructure.Email;
using Chirograph.Infrastructure.Persistence;
using Chirograph.Infrastructure.Storage;
using Chirograph.UnitTests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Chirograph.UnitTests.Infrastructure;

public sealed class DependencyInjectionTests : IDisposable
{
    private readonly TemporaryDirectory _contentRoot = new();

    public void Dispose() => _contentRoot.Dispose();

    [Fact]
    public void The_default_configuration_uses_sqlite_local_disk_and_the_dev_outbox()
    {
        using var provider = Build([]);
        using var scope = provider.CreateScope();

        Assert.IsType<SqliteChirographDbContext>(scope.ServiceProvider.GetRequiredService<IAppDbContext>());
        Assert.IsType<LocalDiskFileStore>(provider.GetRequiredService<IFileStore>());
        Assert.IsType<OutboxEmailSender>(provider.GetRequiredService<IEmailSender>());
        Assert.NotNull(provider.GetRequiredService<IDnsTxtResolver>());
        Assert.NotNull(provider.GetRequiredService<IPdfStamper>());
    }

    [Fact]
    public void Relative_data_paths_are_anchored_to_the_content_root()
    {
        using var provider = Build([]);

        Assert.Equal(Path.Combine(_contentRoot.Path, "App_Data", "files"), provider.GetRequiredService<IOptions<FileStoreOptions>>().Value.RootPath);
        Assert.Equal(Path.Combine(_contentRoot.Path, "App_Data", "outbox"), provider.GetRequiredService<IOptions<EmailOptions>>().Value.OutboxDirectory);
        Assert.True(Directory.Exists(Path.Combine(_contentRoot.Path, "App_Data")));
    }

    [Fact]
    public void Postgres_is_selected_by_configuration()
    {
        using var provider = Build(new()
        {
            ["Database:Provider"] = "Postgres",
            ["ConnectionStrings:Chirograph"] = "Host=localhost;Database=chirograph;Username=app",
        });
        using var scope = provider.CreateScope();

        Assert.IsType<PostgresChirographDbContext>(scope.ServiceProvider.GetRequiredService<IAppDbContext>());
    }

    [Fact]
    public void Postgres_without_a_connection_string_fails_fast_with_guidance()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Build(new() { ["Database:Provider"] = "Postgres" }));

        Assert.Contains("ConnectionStrings__Chirograph", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Smtp_delivery_is_selected_by_configuration()
    {
        using var provider = Build(new() { ["Email:Delivery"] = "Smtp", ["Email:Smtp:Host"] = "smtp.example.org" });

        Assert.IsType<SmtpEmailSender>(provider.GetRequiredService<IEmailSender>());
    }

    private ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddChirographInfrastructure(configuration, _contentRoot.Path);
        return services.BuildServiceProvider(validateScopes: true);
    }
}
