using Chirograph.Domain.Common;

namespace Chirograph.Application.Common;

public static class Errors
{
    public static Error NotFound { get; } = new("not_found", "We couldn't find that.");

    public static Error NotAnActiveMember { get; } =
        new("forbidden", "Your account is not active in this organization. Sign in again or ask an admin.");

    public static Error AdminRequired { get; } = new("forbidden", "Only an admin of your organization can do this.");

    public static Error LinkInvalid { get; } =
        new("link.invalid", "This link isn't valid. It may be incomplete or have been replaced by a newer one. Request a new link.");

    public static Error LinkUsed { get; } = new("link.used", "This link has already been used. Request a new one.");

    public static Error DomainTaken(DomainName domain) =>
        new("organization.domain_taken", $"{domain} is already verified by another organization on Chirograph. Ask one of its admins to invite you.");

    public static Error Validation(string message) => new("validation", message);
}
