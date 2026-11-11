using MimeKit.Cryptography;
using Org.BouncyCastle.Crypto;

namespace Tharga.Team.Email.Tests;

/// <summary>Published DKIM keys without DNS, remembering which names it was asked about.</summary>
internal sealed class InMemoryKeyLocator : IDnsResolver
{
    private readonly Dictionary<string, string> _published = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Asked { get; } = [];

    public InMemoryKeyLocator Publish(string domain, AsymmetricCipherKeyPair key, string selector = DkimTestMail.Selector)
    {
        _published[$"{selector}._domainkey.{domain}"] = DkimTestMail.TxtRecord(key);

        return this;
    }

    public DnsTxtResponse QueryTxt(string domain, CancellationToken cancellationToken = default)
    {
        Asked.Add(domain);

        return _published.TryGetValue(domain, out var record)
            ? new DnsTxtResponse([record])
            : new DnsTxtResponse(DnsQueryStatus.NonExistentDomain);
    }

    public Task<DnsTxtResponse> QueryTxtAsync(string domain, CancellationToken cancellationToken = default)
        => Task.FromResult(QueryTxt(domain, cancellationToken));
}
