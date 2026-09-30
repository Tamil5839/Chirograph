using Chirograph.Domain.Documents;
using Microsoft.Extensions.Options;

namespace Chirograph.Application.Common;

/// <summary>
/// Builds absolute links from <see cref="ChirographOptions.PublicBaseUrl"/>. The verification path is printed on
/// paper, so it is a permanent public contract.
/// </summary>
public sealed class LinkBuilder(IOptions<ChirographOptions> options)
{
    public const string VerifyPath = "/v";
    public const string MagicLinkPath = "/auth/link";
    public const string EmployeePortalPath = "/me";

    public Uri VerifyPage => Absolute(VerifyPath);

    public Uri EmployeePortal => Absolute(EmployeePortalPath);

    public Uri Verification(VerificationId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Absolute($"{VerifyPath}/{id.Value}");
    }

    public Uri MagicLink(string rawToken) => Absolute($"{MagicLinkPath}?token={Uri.EscapeDataString(rawToken)}");

    private Uri Absolute(string pathAndQuery) =>
        new(options.Value.PublicBaseUrl.GetLeftPart(UriPartial.Path).TrimEnd('/') + pathAndQuery);
}
