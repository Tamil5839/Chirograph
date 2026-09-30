namespace Chirograph.Application.Common;

public sealed class ChirographOptions
{
    public const string Section = "Chirograph";

    /// <summary>
    /// The public address of this Chirograph installation. It is printed on every stamped document and used in every
    /// emailed link, so it comes from configuration and never from the incoming request's Host header.
    /// </summary>
    public Uri PublicBaseUrl { get; set; } = new("https://localhost:7228");

    /// <summary>Largest PDF accepted for issuing or checking.</summary>
    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;
}

/// <summary>How long each kind of emailed link stays valid. Every link also works only once.</summary>
public static class LinkLifetimes
{
    public static readonly TimeSpan SignUpConfirmation = TimeSpan.FromHours(24);
    public static readonly TimeSpan StaffSignIn = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan TeamInvite = TimeSpan.FromHours(72);
    public static readonly TimeSpan DomainConfirmation = TimeSpan.FromHours(24);
    public static readonly TimeSpan EmployeeSignIn = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan EmployeeWelcome = TimeSpan.FromHours(72);
}
