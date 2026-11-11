using System.Collections.Concurrent;
using DnsClient;
using MimeKit.Cryptography;

namespace Tharga.Team.Email;

/// <summary>
/// Finds the DKIM key records published in DNS, at <c>{selector}._domainkey.{domain}</c>, and keeps them briefly.
/// </summary>
/// <remarks>
/// <b>Every failure is a missing key, never an error.</b> A resolver that is down, slow or answering garbage
/// leaves the mail unverified — which only means it opens unassigned — so nothing here may throw past the
/// verifier or hold up reading the mailbox. Each lookup is cut off after <see cref="DefaultLookupTimeout"/>.
/// The records are handed over as published; MimeKit reads the key out of them and passes over the rest.
/// <para>
/// <b>Cached per process, misses too.</b> A support mailbox hears from the same few providers all day, and a
/// key is published for months, so <see cref="KeyLifetime"/> saves nearly every lookup. A miss is kept for
/// <see cref="MissLifetime"/> so a dead resolver costs one timeout per selector per minute rather than one per
/// mail. The cache is emptied when it reaches <see cref="MaxCachedNames"/>, because the names in it come from
/// whoever writes to the mailbox.
/// </para>
/// </remarks>
internal sealed class DnsDkimPublicKeyLocator : IDnsResolver
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
    private readonly ConcurrentDictionary<string, (DnsTxtResponse Response, DateTimeOffset Until)> _cache = new(StringComparer.OrdinalIgnoreCase);

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

    public DnsTxtResponse QueryTxt(string domain, CancellationToken cancellationToken = default)
        => QueryTxtAsync(domain, cancellationToken).GetAwaiter().GetResult();

    public async Task<DnsTxtResponse> QueryTxtAsync(string domain, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();

        if (!_cache.TryGetValue(domain, out var cached) || cached.Until <= now)
        {
            var response = await FetchAsync(domain, cancellationToken);

            if (_cache.Count >= MaxCachedNames) _cache.Clear();

            cached = (response, now + (response.Records.Count == 0 ? MissLifetime : KeyLifetime));
            _cache[domain] = cached;
        }

        return cached.Response;
    }

    private async Task<DnsTxtResponse> FetchAsync(string name, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_lookupTimeout);

        try
        {
            var records = await _lookup(name, timeout.Token).WaitAsync(timeout.Token);

            return new DnsTxtResponse((records ?? []).Where(x => x != null));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new DnsTxtResponse(DnsQueryStatus.TemporaryFailure);
        }
    }

    private static async Task<IReadOnlyList<string>> LookupTxtAsync(string name, CancellationToken cancellationToken)
    {
        var response = await Resolver.Value.QueryAsync(name, QueryType.TXT, QueryClass.IN, cancellationToken);

        return [.. response.Answers.TxtRecords().Select(x => string.Concat(x.Text))];
    }
}
