using System.Text.RegularExpressions;
using Chirograph.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Chirograph.Infrastructure.Storage;

public sealed class FileStoreOptions
{
    public const string Section = "Storage";

    /// <summary>Directory for stored files. Relative paths are resolved against the app's content root.</summary>
    public string RootPath { get; set; } = "App_Data/files";
}

/// <summary>
/// Stores files on local disk for development and single-server deployments. Writes go to a temporary file first
/// and are then moved into place, so a crash never leaves a half-written PDF under a real key.
/// </summary>
public sealed partial class LocalDiskFileStore : IFileStore
{
    private readonly string _root;

    public LocalDiskFileStore(IOptions<FileStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _root = Path.GetFullPath(options.Value.RootPath);
        Directory.CreateDirectory(_root);
    }

    public async Task SaveAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        File.Delete(Resolve(key));
        return Task.CompletedTask;
    }

    private string Resolve(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!KeyPattern().IsMatch(key) || key.Contains("..", StringComparison.Ordinal) || key.Contains("//", StringComparison.Ordinal))
            throw new ArgumentException($"'{key}' is not a valid file key.", nameof(key));

        var path = Path.GetFullPath(Path.Combine(_root, key));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException($"'{key}' is not a valid file key.", nameof(key));
        return path;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9/_.-]{0,199}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
