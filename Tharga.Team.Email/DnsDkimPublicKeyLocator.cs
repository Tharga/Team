using System.Collections.Concurrent;
using DnsClient;
using MimeKit.Cryptography;
using Org.BouncyCastle.Crypto;

namespace Tharga.Team.Email;

/// <summary>
/// Finds a DKIM public key in DNS, at <c>{selector}._domainkey.{domain}</c>, and keeps it briefly.
/// </summary>
/// <remarks>
/// <b>Every failure is a missing key, never an error.</b> A resolver that is down, slow or answering garbage
/// leaves the mail unverified — which only means it opens unassigned — so nothing here may throw past the
/// verifier or hold up reading the mailbox. Each lookup is cut off after <see cref="DefaultLookupTimeout"/>.
/// <para>
/// <b>Cached per process, misses too.</b> A support mailbox hears from the same few providers all day, and a
/// key is published for months, so <see cref="KeyLifetime"/> saves nearly every lookup. A miss is kept for
/// <see cref="MissLifetime"/> so a dead resolver costs one timeout per selector per minute rather than one per
/// mail. The cache is emptied when it reaches <see cref="MaxCachedNames"/>, because the names in it come from
/// whoever writes to the mailbox.
/// </para>
/// </remarks>
internal sealed class DnsDkimPublicKeyLocator : DkimPublicKeyLocatorBase
{
    internal static readonly TimeSpan KeyLifetime = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan MissLifetime = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan DefaultLookupTimeout = TimeSpan.FromSeconds(5);
    internal const int MaxCachedNames = 1000;

    private static readonly Lazy<LookupClient> Resolver = new(() => new LookupClient(new LookupClientOptions
    {
        Timeout = TimeSpan.FromSeconds(2),
        Retries = 1,
        UseCache = false,
        ThrowDnsErrors = false
    }));

    private readonly Func<string, CancellationToken, Task<IReadOnlyList<string>>> _lookup;
    private readonly TimeProvider _time;
    private readonly TimeSpan _lookupTimeout;
    private readonly ConcurrentDictionary<string, (AsymmetricKeyParameter Key, DateTimeOffset Until)> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="lookup">The TXT records published at a name, each with its strings joined.</param>
    /// <param name="time">The clock the cache expires by.</param>
    /// <param name="lookupTimeout">How long one lookup may take before it counts as a miss.</param>
    internal DnsDkimPublicKeyLocator(Func<string, CancellationToken, Task<IReadOnlyList<string>>> lookup, TimeProvider time = null, TimeSpan? lookupTimeout = null)
    {
        _lookup = lookup;
        _time = time ?? TimeProvider.System;
        _lookupTimeout = lookupTimeout ?? DefaultLookupTimeout;
    }

    /// <summary>A locator asking the system's DNS servers.</summary>
    public static DnsDkimPublicKeyLocator Create() => new(LookupTxtAsync);

    public override AsymmetricKeyParameter LocatePublicKey(string methods, string domain, string selector, CancellationToken cancellationToken = default)
        => LocatePublicKeyAsync(methods, domain, selector, cancellationToken).GetAwaiter().GetResult();

    public override async Task<AsymmetricKeyParameter> LocatePublicKeyAsync(string methods, string domain, string selector, CancellationToken cancellationToken = default)
    {
        var name = $"{selector}._domainkey.{domain}";
        var now = _time.GetUtcNow();

        if (!_cache.TryGetValue(name, out var cached) || cached.Until <= now)
        {
            var key = await FetchAsync(name, cancellationToken);

            if (_cache.Count >= MaxCachedNames) _cache.Clear();

            cached = (key, now + (key == null ? MissLifetime : KeyLifetime));
            _cache[name] = cached;
        }

        return cached.Key ?? throw new InvalidOperationException("No usable DKIM key is published for the signature's selector.");
    }

    private async Task<AsymmetricKeyParameter> FetchAsync(string name, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_lookupTimeout);

        IReadOnlyList<string> records;

        try
        {
            records = await _lookup(name, timeout.Token).WaitAsync(timeout.Token);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        foreach (var record in records ?? [])
        {
            try
            {
                if (record?.Contains("p=", StringComparison.Ordinal) == true) return GetPublicKey(record);
            }
            catch (Exception)
            {
                // A revoked key (an empty p=) or a malformed record is the same as none; another record may still serve.
            }
        }

        return null;
    }

    private static async Task<IReadOnlyList<string>> LookupTxtAsync(string name, CancellationToken cancellationToken)
    {
        var response = await Resolver.Value.QueryAsync(name, QueryType.TXT, QueryClass.IN, cancellationToken);

        return [.. response.Answers.TxtRecords().Select(x => string.Concat(x.Text))];
    }
}
