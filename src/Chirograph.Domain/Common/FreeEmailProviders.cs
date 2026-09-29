using System.Collections.Frozen;

namespace Chirograph.Domain.Common;

/// <summary>Public mailbox providers. Nobody can register an organization on one of these domains.</summary>
public static class FreeEmailProviders
{
    private static readonly FrozenSet<string> Domains = new[]
    {
        "gmail.com", "googlemail.com", "yahoo.com", "yahoo.co.in", "yahoo.in", "yahoo.co.uk", "ymail.com",
        "rocketmail.com", "outlook.com", "outlook.in", "hotmail.com", "hotmail.co.uk", "live.com", "live.in",
        "msn.com", "icloud.com", "me.com", "mac.com", "aol.com", "proton.me", "protonmail.com", "pm.me",
        "gmx.com", "gmx.net", "mail.com", "yandex.com", "yandex.ru", "zohomail.com", "zohomail.in",
        "rediffmail.com", "tutanota.com", "tuta.io", "fastmail.com", "hey.com", "qq.com", "163.com",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static bool Contains(DomainName domain) => Domains.Contains(domain.Value);
}
