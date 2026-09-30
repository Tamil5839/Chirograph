// Makes a tampered copy of a PDF to demonstrate Chirograph's exact-file check.
//
//   dotnet run scripts/tamper.cs -- <input.pdf> <output.pdf>
//       Changes a single byte inside the PDF's binary comment line. The copy opens and looks exactly the same,
//       but it is no longer the issued file, so verification fails.
//
//   dotnet run scripts/tamper.cs -- <input.pdf> <output.pdf> --replace Associate Principal
//       Replaces text of the same length (e.g. a designation or a year) to make a convincing visible forgery.
//       Works on the sample letter, whose text is stored uncompressed.

using System.Security.Cryptography;
using System.Text;

if (args.Length is not (2 or 5) || (args.Length == 5 && args[2] != "--replace"))
{
    Console.Error.WriteLine("Usage: dotnet run scripts/tamper.cs -- <input.pdf> <output.pdf> [--replace OLD NEW]");
    return 1;
}

var input = File.ReadAllBytes(args[0]);
var output = input.ToArray();

if (args.Length == 5)
{
    var (oldText, newText) = (args[3], args[4]);
    if (oldText.Length != newText.Length)
    {
        Console.Error.WriteLine("OLD and NEW must have the same length, so the PDF's internal offsets stay valid.");
        return 1;
    }
    var content = Encoding.Latin1.GetString(input);
    var occurrences = (content.Length - content.Replace(oldText, "", StringComparison.Ordinal).Length) / oldText.Length;
    if (occurrences == 0)
    {
        Console.Error.WriteLine($"'{oldText}' does not appear as plain text in {args[0]}.");
        return 1;
    }
    output = Encoding.Latin1.GetBytes(content.Replace(oldText, newText, StringComparison.Ordinal));
    Console.WriteLine($"Replaced {occurrences} occurrence(s) of '{oldText}' with '{newText}'.");
}
else
{
    // A PDF's second line is a comment of high-bit bytes (e.g. "%âãÏÓ") marking the file as binary. Changing one of
    // them leaves the document valid and visually identical.
    var lineEnd = Array.IndexOf(output, (byte)'\n');
    var index = lineEnd >= 0 && lineEnd + 2 < output.Length && output[lineEnd + 1] == (byte)'%' && output[lineEnd + 2] >= 0x80
        ? lineEnd + 2
        : output.Length / 2;
    var before = output[index];
    output[index] ^= 0x01;
    Console.WriteLine($"Changed 1 byte at offset {index}: 0x{before:X2} -> 0x{output[index]:X2}.");
}

File.WriteAllBytes(args[1], output);
Console.WriteLine($"SHA-256 before: {Convert.ToHexStringLower(SHA256.HashData(input))}");
Console.WriteLine($"SHA-256 after:  {Convert.ToHexStringLower(SHA256.HashData(output))}");
Console.WriteLine($"Wrote {args[1]}. Upload it on the verification page: the check will fail.");
return 0;
