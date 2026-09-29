using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Chirograph.Domain.Common;

/// <summary>
/// An email address normalised to lower case, used as an identity (HR members and employees sign in with it).
/// Only plain ASCII "dot-atom" local parts are accepted; the domain part follows <see cref="DomainName"/> rules.
/// </summary>
public sealed partial record EmailAddress
{
    public const int MaxLength = 254;
    private const int LocalPartMaxLength = 64;

    private EmailAddress(string localPart, DomainName domain)
    {
        LocalPart = localPart;
        Domain = domain;
    }

    public string LocalPart { get; }

    public DomainName Domain { get; }

    public string Value => $"{LocalPart}@{Domain.Value}";

    public bool IsOn(DomainName domain) => Domain == domain;

    public static EmailAddress Parse(string? input) =>
        TryParse(input, out var email, out var error) ? email : throw new DomainException("email.invalid", error);

    public static bool TryParse(
        string? input,
        [NotNullWhen(true)] out EmailAddress? email,
        [NotNullWhen(false)] out string? error)
    {
        email = null;
        var candidate = input?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(candidate))
        {
            error = "Enter an email address.";
            return false;
        }

        var at = candidate.LastIndexOf('@');
        if (at <= 0 || at == candidate.Length - 1 || candidate.Length > MaxLength)
        {
            error = $"'{candidate}' is not a valid email address.";
            return false;
        }

        var localPart = candidate[..at];
        if (localPart.Length > LocalPartMaxLength || !LocalPartPattern().IsMatch(localPart))
        {
            error = $"'{candidate}' is not a valid email address.";
            return false;
        }
        if (!DomainName.TryParse(candidate[(at + 1)..], out var domain, out _))
        {
            error = $"'{candidate}' does not have a valid domain.";
            return false;
        }

        email = new EmailAddress(localPart, domain);
        error = null;
        return true;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z0-9!#$%&'*+/=?^_`{|}~-]+(\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex LocalPartPattern();
}
