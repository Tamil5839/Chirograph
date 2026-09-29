using System.Net;
using Chirograph.Application.Abstractions;
using DnsClient;
using Microsoft.Extensions.Options;

namespace Chirograph.Infrastructure.Dns;

public sealed class DnsOptions
{
    public const string Section = "Dns";

    /// <summary>Resolver IP addresses to query. Empty means the machine's configured resolvers.</summary>
    public IList<string> NameServers { get; } = [];
}

/// <summary>Looks up TXT records with DnsClient, bypassing its cache so a freshly published record is seen.</summary>
public sealed class DnsClientTxtResolver : IDnsTxtResolver
{
    private readonly LookupClient _client;

    public DnsClientTxtResolver(IOptions<DnsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var servers = options.Value.NameServers.Select(IPAddress.Parse).ToArray();
        var clientOptions = servers.Length > 0 ? new LookupClientOptions(servers) : new LookupClientOptions();
        clientOptions.UseCache = false;
        clientOptions.Timeout = TimeSpan.FromSeconds(5);
        clientOptions.Retries = 1;
        clientOptions.ThrowDnsErrors = false;
        _client = new LookupClient(clientOptions);
    }

    public async Task<IReadOnlyList<string>> GetTxtRecordsAsync(string hostName, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.QueryAsync(hostName, QueryType.TXT, cancellationToken: cancellationToken).ConfigureAwait(false);
            return response.Answers.TxtRecords().Select(record => string.Concat(record.Text)).ToList();
        }
        catch (DnsResponseException)
        {
            return [];
        }
    }
}
