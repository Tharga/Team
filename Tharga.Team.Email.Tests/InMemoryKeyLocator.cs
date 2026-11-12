using MimeKit.Cryptography;
using Org.BouncyCastle.Crypto;

namespace Tharga.Team.Email.Tests;

/// <summary>Published DKIM keys without DNS, remembering which domains it was asked about.</summary>
internal sealed class InMemoryKeyLocator : IDkimPublicKeyLocator
{
    private readonly Dictionary<string, AsymmetricKeyParameter> _published = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Asked { get; } = [];

    public InMemoryKeyLocator Publish(string domain, AsymmetricCipherKeyPair key, string selector = DkimTestMail.Selector)
    {
        _published[$"{selector}._domainkey.{domain}"] = key.Public;

        return this;
    }

    public AsymmetricKeyParameter LocatePublicKey(string methods, string domain, string selector, CancellationToken cancellationToken = default)
    {
        Asked.Add(domain);

        return _published.TryGetValue($"{selector}._domainkey.{domain}", out var key)
            ? key
            : throw new InvalidOperationException("No key is published there.");
    }

    public Task<AsymmetricKeyParameter> LocatePublicKeyAsync(string methods, string domain, string selector, CancellationToken cancellationToken = default)
        => Task.FromResult(LocatePublicKey(methods, domain, selector, cancellationToken));
}
