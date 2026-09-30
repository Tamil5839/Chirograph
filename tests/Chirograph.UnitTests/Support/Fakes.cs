using System.Collections.Concurrent;
using Chirograph.Application.Abstractions;

namespace Chirograph.UnitTests.Support;

internal sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public bool Fail { get; set; }

    public IReadOnlyList<EmailMessage> Sent => _sent.ToList();

    public EmailMessage Last => _sent.Last();

    public IReadOnlyList<EmailMessage> To(string address) => _sent.Where(m => m.To.Value == address).ToList();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (Fail)
            throw new InvalidOperationException("SMTP server unavailable.");
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}

internal sealed class FakeDnsTxtResolver : IDnsTxtResolver
{
    private readonly ConcurrentDictionary<string, List<string>> _records = new(StringComparer.OrdinalIgnoreCase);

    public void Publish(string hostName, string value) => _records.GetOrAdd(hostName, _ => []).Add(value);

    public Task<IReadOnlyList<string>> GetTxtRecordsAsync(string hostName, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(_records.TryGetValue(hostName, out var values) ? values.ToList() : []);
}

internal sealed class InMemoryFileStore : IFileStore
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, byte[]> Files => _files;

    public Task SaveAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken = default) =>
        _files.TryAdd(key, content.ToArray()) ? Task.CompletedTask : throw new IOException($"{key} already exists.");

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(_files.TryGetValue(key, out var content) ? new MemoryStream(content, writable: false) : null);

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _files.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
