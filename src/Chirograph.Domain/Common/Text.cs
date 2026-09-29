using System.Text;

namespace Chirograph.Domain.Common;

/// <summary>Normalisation and validation for free-text fields that are stored and later displayed to third parties.</summary>
public static class Text
{
    /// <summary>Trims, collapses runs of whitespace to a single space and requires a non-empty value of bounded length.</summary>
    public static string Required(string? value, int maxLength, string fieldName)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0)
            throw new DomainException("validation.required", $"Enter the {fieldName}.");
        if (normalized.Length > maxLength)
            throw new DomainException("validation.too_long", $"The {fieldName} must be at most {maxLength} characters.");
        return normalized;
    }

    /// <summary>Like <see cref="Required"/> but returns <c>null</c> for blank input and truncates instead of failing.</summary>
    public static string? Optional(string? value, int maxLength)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0)
            return null;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength].TrimEnd();
    }

    /// <summary>
    /// Removes control characters and Unicode bidirectional overrides (which could make a name render
    /// differently from how it is stored) and collapses whitespace. Joiners used by Indic scripts are kept.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }
            if (char.IsControl(c) || IsBidiControl(c))
                continue;
            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }
            builder.Append(c);
        }
        return builder.ToString();
    }

    private static bool IsBidiControl(char c) =>
        c is '‎' or '‏' or '؜' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩');
}
