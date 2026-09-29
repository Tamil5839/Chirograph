using Chirograph.Infrastructure.Storage;
using Chirograph.UnitTests.Support;
using Microsoft.Extensions.Options;

namespace Chirograph.UnitTests.Infrastructure;

public sealed class LocalDiskFileStoreTests : IDisposable
{
    private readonly TemporaryDirectory _directory = new();
    private readonly LocalDiskFileStore _store;

    public LocalDiskFileStoreTests() =>
        _store = new LocalDiskFileStore(Options.Create(new FileStoreOptions { RootPath = _directory.Path }));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task Stored_files_read_back_byte_for_byte()
    {
        var content = new byte[200_000];
        new Random(3).NextBytes(content);

        await _store.SaveAsync("documents/org1/letter.pdf", content, "application/pdf", Token);
        await using var stream = await _store.OpenReadAsync("documents/org1/letter.pdf", Token);

        Assert.NotNull(stream);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, Token);
        Assert.Equal(content, copy.ToArray());
        Assert.Empty(Directory.GetFiles(_directory.Path, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_missing_file_reads_as_null()
    {
        Assert.Null(await _store.OpenReadAsync("documents/none.pdf", Token));
    }

    [Fact]
    public async Task Files_are_never_overwritten()
    {
        await _store.SaveAsync("documents/a.pdf", new byte[] { 1 }, "application/pdf", Token);

        await Assert.ThrowsAnyAsync<IOException>(() => _store.SaveAsync("documents/a.pdf", new byte[] { 2 }, "application/pdf", Token));

        await using var stream = await _store.OpenReadAsync("documents/a.pdf", Token);
        Assert.Equal(1, stream!.ReadByte());
    }

    [Fact]
    public async Task Deleted_files_are_gone()
    {
        await _store.SaveAsync("documents/a.pdf", new byte[] { 1 }, "application/pdf", Token);

        await _store.DeleteAsync("documents/a.pdf", Token);

        Assert.Null(await _store.OpenReadAsync("documents/a.pdf", Token));
    }

    [Theory]
    [InlineData("../outside.pdf")]
    [InlineData("documents/../../outside.pdf")]
    [InlineData("/etc/passwd")]
    [InlineData("documents//a.pdf")]
    [InlineData("Documents/a.pdf")]
    [InlineData("documents/a b.pdf")]
    [InlineData("")]
    public async Task Keys_cannot_escape_the_storage_folder(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SaveAsync(key, new byte[] { 1 }, "application/pdf", Token));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.OpenReadAsync(key, Token));
    }
}
