namespace Chirograph.Application.Abstractions;

public interface IDnsTxtResolver
{
    /// <summary>
    /// Returns the TXT records published at <paramref name="hostName"/> (each record's strings joined), or an empty
    /// list if there are none or the name does not exist.
    /// </summary>
    Task<IReadOnlyList<string>> GetTxtRecordsAsync(string hostName, CancellationToken cancellationToken = default);
}
