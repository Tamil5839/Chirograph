using Chirograph.Domain.Common;
using Chirograph.Domain.Documents;

namespace Chirograph.UnitTests.Domain;

public class DocumentFingerprintTests
{
    [Theory]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq", "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1")]
    public void Compute_matches_the_published_SHA256_test_vectors(string input, string expectedHex)
    {
        var fingerprint = DocumentFingerprint.Compute(System.Text.Encoding.ASCII.GetBytes(input));

        Assert.Equal(expectedHex, fingerprint.Hex);
    }

    [Fact]
    public async Task Hashing_a_stream_gives_the_same_result_as_hashing_the_bytes()
    {
        var content = new byte[3 * 1024 * 1024 + 17];
        new Random(42).NextBytes(content);

        var fromBytes = DocumentFingerprint.Compute(content);
        var fromStream = await DocumentFingerprint.ComputeAsync(new MemoryStream(content), TestContext.Current.CancellationToken);

        Assert.Equal(fromBytes, fromStream);
    }

    [Fact]
    public void Changing_any_single_bit_of_the_file_changes_the_fingerprint()
    {
        var original = new byte[512];
        new Random(7).NextBytes(original);
        var originalFingerprint = DocumentFingerprint.Compute(original);

        for (var position = 0; position < original.Length; position++)
        {
            var tampered = (byte[])original.Clone();
            tampered[position] ^= 0x01;

            Assert.False(
                originalFingerprint.Matches(DocumentFingerprint.Compute(tampered)),
                $"Flipping one bit at byte {position} went undetected.");
        }
    }

    [Fact]
    public void Adding_or_removing_a_byte_changes_the_fingerprint()
    {
        var original = TestDataBytes();
        var fingerprint = DocumentFingerprint.Compute(original);

        Assert.False(fingerprint.Matches(DocumentFingerprint.Compute([.. original, 0x0A])));
        Assert.False(fingerprint.Matches(DocumentFingerprint.Compute(original.AsSpan(0, original.Length - 1))));
    }

    [Fact]
    public void Identical_content_always_matches()
    {
        var content = TestDataBytes();

        Assert.True(DocumentFingerprint.Compute(content).Matches(DocumentFingerprint.Compute(content.ToArray())));
    }

    [Fact]
    public void FromHex_round_trips_and_normalises_case()
    {
        var fingerprint = DocumentFingerprint.Compute(TestDataBytes());

        var parsed = DocumentFingerprint.FromHex("  " + fingerprint.Hex.ToUpperInvariant() + " ");

        Assert.Equal(fingerprint, parsed);
        Assert.Equal(fingerprint.Hex, parsed.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85")]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b8555")]
    [InlineData("g3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    public void FromHex_rejects_anything_but_64_hex_digits(string hex)
    {
        var error = Assert.Throws<DomainException>(() => DocumentFingerprint.FromHex(hex));

        Assert.Equal("fingerprint.invalid", error.Code);
    }

    private static byte[] TestDataBytes() => "%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\n%%EOF"u8.ToArray();
}
