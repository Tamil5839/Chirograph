using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Chirograph.Domain.Common;

/// <summary>
/// A DNS domain such as <c>acme.com</c>, normalised to lower-case ASCII. Internationalised names are kept in
/// their punycode (<c>xn--…</c>) form so a look-alike made from another script's letters is visibly different.
/// </summary>
public sealed partial record DomainName
{
    public const int MaxLength = 253;

    private static readonly IdnMapping Idn = new() { AllowUnassigned = false, UseStd3AsciiRules = true };

    private DomainName(string value) => Value = value;

    public string Value { get; }

    public bool IsInternationalized => Value.Split('.').Any(label => label.StartsWith("xn--", StringComparison.Ordinal));

    public string ToUnicode() => Idn.GetUnicode(Value);

    public static DomainName Parse(string? input) =>
        TryParse(input, out var domain, out var error) ? domain : throw new DomainException("domain.invalid", error);

    public static bool TryParse(
        string? input,
        [NotNullWhen(true)] out DomainName? domain,
        [NotNullWhen(false)] out string? error)
    {
        domain = null;
        var candidate = input?.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(candidate))
        {
            error = "Enter a domain name, for example acme.com.";
            return false;
        }
        if (candidate.IndexOfAny([':', '/', '@', ' ', '\\', '?', '#']) >= 0)
        {
            error = "Enter just the domain name, for example acme.com (no https://, paths or email addresses).";
            return false;
        }

        string ascii;
        try
        {
            ascii = Idn.GetAscii(candidate);
        }
        catch (ArgumentException)
        {
            error = $"'{candidate}' is not a valid domain name.";
            return false;
        }

        var labels = ascii.Split('.');
        if (labels.Length < 2)
        {
            error = "Enter a full domain name including its ending, for example acme.com.";
            return false;
        }
        if (ascii.Length > MaxLength || !labels.All(label => LabelPattern().IsMatch(label)))
        {
            error = $"'{candidate}' is not a valid domain name.";
            return false;
        }
        if (labels[^1].All(char.IsAsciiDigit))
        {
            error = "Enter a domain name, not an IP address.";
            return false;
        }

        domain = new DomainName(ascii);
        error = null;
        return true;
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex LabelPattern();
}
