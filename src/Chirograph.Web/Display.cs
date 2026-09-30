using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Chirograph.Domain.Documents;
using Chirograph.Domain.Organizations;
using Microsoft.AspNetCore.Html;

namespace Chirograph.Web;

/// <summary>Consistent, culture-invariant formatting for views. Times are shown in UTC and labelled as such.</summary>
public static partial class Display
{
    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public static string Date(DateTimeOffset moment) => moment.UtcDateTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public static string DateTime(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("d MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);

    public static string Period(DateOnly start, DateOnly end) => $"{Date(start)} – {Date(end)}";

    public static string Type(DocumentType type) => type.DisplayName();

    public static string VerificationMethod(DomainVerificationMethod? method) => method switch
    {
        DomainVerificationMethod.DnsTxtRecord => "a DNS record published on the domain",
        DomainVerificationMethod.AdminMailbox => "a confirmation from the domain's administrative mailbox",
        _ => "an unknown method",
    };

    public static string Role(MemberRole role) => role == MemberRole.Admin ? "Admin" : "Issuer";

    public static string FileSize(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / (1024.0 * 1024):0.0} MB"
        : $"{Math.Max(1, bytes / 1024)} KB";

    /// <summary>HTML-encodes plain text and turns http(s) URLs into links (used by the development mailbox).</summary>
    public static IHtmlContent Linkify(string text)
    {
        var encoded = WebUtility.HtmlEncode(text);
        return new HtmlString(UrlPattern().Replace(encoded, match => $"<a href=\"{match.Value}\">{match.Value}</a>"));
    }

    [GeneratedRegex(@"https?://[^\s<]+", RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();
}
