using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Chirograph.Domain.Common;

namespace Chirograph.Domain.Documents;

/// <summary>
/// The public, non-guessable identifier printed on a document and encoded in its QR code.
/// 25 characters of Crockford Base32 carry 125 bits from a cryptographic RNG, so IDs cannot be enumerated.
/// </summary>
public sealed record VerificationId
{
    public const int Length = 25;
    public const int GroupSize = 5;

    /// <summary>Crockford's Base32 alphabet: digits and upper-case letters without I, L, O and U.</summary>
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private const int MaxInputLength = 64;

    private VerificationId(string value) => Value = value;

    /// <summary>Canonical form: 25 upper-case characters, no separators. Used in URLs and storage.</summary>
    public string Value { get; }

    public static VerificationId New() => new(RandomNumberGenerator.GetString(Alphabet, Length));

    public static VerificationId Parse(string? input) =>
        TryParse(input, out var id)
            ? id
            : throw new DomainException("verification_id.invalid", "That is not a valid Chirograph verification ID.");

    /// <summary>
    /// Accepts what a person might type from a printed letter: any case, hyphens or spaces between groups, and the
    /// easily confused letters O (read as zero) and I/L (read as one).
    /// </summary>
    public static bool TryParse(string? input, [NotNullWhen(true)] out VerificationId? id)
    {
        id = null;
        if (input is null || input.Length > MaxInputLength)
            return false;

        Span<char> buffer = stackalloc char[Length];
        var count = 0;
        foreach (var raw in input)
        {
            if (raw == '-' || char.IsWhiteSpace(raw))
                continue;
            if (!char.IsAscii(raw) || count == Length)
                return false;

            var c = char.ToUpperInvariant(raw) switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                var other => other,
            };
            if (!Alphabet.Contains(c))
                return false;
            buffer[count++] = c;
        }

        if (count != Length)
            return false;

        id = new VerificationId(new string(buffer));
        return true;
    }

    /// <summary>Grouped form for printing and reading aloud, e.g. <c>7K3M9-QXAB2-C4DEF-GH8JK-MNP2Q</c>.</summary>
    public string ToDisplayString() => string.Join('-', Value.Chunk(GroupSize).Select(group => new string(group)));

    public override string ToString() => Value;
}
