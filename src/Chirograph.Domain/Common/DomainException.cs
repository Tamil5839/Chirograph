namespace Chirograph.Domain.Common;

/// <summary>
/// A business rule was violated. <see cref="Exception.Message"/> is written for end users and is safe to display;
/// <see cref="Code"/> is a stable identifier for programmatic handling and tests.
/// </summary>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
