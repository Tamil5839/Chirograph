using System.Security.Cryptography;
using Chirograph.Domain.Common;

namespace Chirograph.Domain.Documents;

/// <summary>
/// SHA-256 of a document's exact bytes. Any change to the file, even a single bit, produces a different fingerprint.
/// </summary>
public sealed record DocumentFingerprint
{
    public const int HexLength = 64;

    private DocumentFingerprint(string hex) => Hex = hex;

    /// <summary>Lower-case hexadecimal digest, the same format <c>sha256sum</c> prints.</summary>
    public string Hex { get; }

    public static DocumentFingerprint Compute(ReadOnlySpan<byte> content) =>
        new(Convert.ToHexStringLower(SHA256.HashData(content)));

    /// <summary>Hashes a stream incrementally, so large uploads never need to be buffered in memory.</summary>
    public static async Task<DocumentFingerprint> ComputeAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var hash = await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false);
        return new DocumentFingerprint(Convert.ToHexStringLower(hash));
    }

    public static DocumentFingerprint FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        var normalized = hex.Trim().ToLowerInvariant();
        if (normalized.Length != HexLength || !normalized.All(char.IsAsciiHexDigitLower))
            throw new DomainException("fingerprint.invalid", "A SHA-256 fingerprint is 64 hexadecimal characters.");
        return new DocumentFingerprint(normalized);
    }

    public bool Matches(DocumentFingerprint other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(Hex, other.Hex, StringComparison.Ordinal);
    }

    public override string ToString() => Hex;
}
