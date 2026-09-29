using System.Globalization;
using Chirograph.Domain.Common;
using Chirograph.Domain.Documents;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Chirograph.Infrastructure.Persistence;

/// <summary>Maps domain value objects to their canonical string forms.</summary>
public static class ValueConverters
{
    public sealed class DomainNameConverter() : ValueConverter<DomainName, string>(
        domain => domain.Value,
        value => DomainName.Parse(value));

    public sealed class EmailAddressConverter() : ValueConverter<EmailAddress, string>(
        email => email.Value,
        value => EmailAddress.Parse(value));

    public sealed class VerificationIdConverter() : ValueConverter<VerificationId, string>(
        id => id.Value,
        value => VerificationId.Parse(value));

    public sealed class DocumentFingerprintConverter() : ValueConverter<DocumentFingerprint, string>(
        fingerprint => fingerprint.Hex,
        value => DocumentFingerprint.FromHex(value));

    /// <summary>
    /// UTC timestamps as <c>yyyy-MM-ddTHH:mm:ss.ffffffZ</c>: fixed width, so text order is time order. Microsecond
    /// precision matches PostgreSQL's <c>timestamptz</c>, so both providers store exactly the same instants.
    /// </summary>
    public sealed class UtcIsoTimestampConverter() : ValueConverter<DateTimeOffset, string>(
        timestamp => timestamp.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture),
        value => new DateTimeOffset(DateTime.ParseExact(value, Format, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), TimeSpan.Zero))
    {
        private const string Format = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";
    }
}
