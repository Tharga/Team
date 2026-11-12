namespace Tharga.Team.Email.Tests;

/// <summary>
/// Finding a DKIM key in DNS: where it looks, how long it keeps what it found, and that nothing a resolver does
/// can hold up or break reading the mailbox.
/// </summary>
/// <remarks>
/// The DNS query itself is the one part replaced, so the record parsing, caching and time-boxing are the
/// production code's own.
/// </remarks>
public class DnsDkimPublicKeyLocatorTests
{
    private const string Name = "s1._domainkey.example.com";

    private readonly ManualClock _clock = new();
    private readonly List<string> _asked = [];

    private DnsDkimPublicKeyLocator Locator(params string[] records)
        => new((name, _) =>
        {
            _asked.Add(name);
            return Task.FromResult<IReadOnlyList<string>>(records);
        }, _clock);

    [Fact]
    public async Task APublishedKey_VerifiesAMailSignedWithIt()
    {
        var verifier = new DkimSenderVerifier(Locator(DkimTestMail.TxtRecord(DkimTestMail.Example)));

        Assert.Equal("example.com", await verifier.VerifiedDomainAsync(DkimTestMail.Signed("example.com")));
        Assert.Equal([Name], _asked);
    }

    [Fact]
    public async Task AKeyFound_IsKeptRatherThanLookedUpForEveryMail()
    {
        var locator = Locator(DkimTestMail.TxtRecord(DkimTestMail.Example));

        await locator.LocatePublicKeyAsync("dns/txt", "example.com", "s1");
        _clock.Advance(DnsDkimPublicKeyLocator.KeyLifetime - TimeSpan.FromSeconds(1));
        await locator.LocatePublicKeyAsync("dns/txt", "example.com", "s1");

        Assert.Single(_asked);
    }

    /// <summary>Kept briefly, so a rotated key is picked up within minutes.</summary>
    [Fact]
    public async Task AKeyFound_IsLookedUpAgainOnceItHasAged()
    {
        var locator = Locator(DkimTestMail.TxtRecord(DkimTestMail.Example));

        await locator.LocatePublicKeyAsync("dns/txt", "example.com", "s1");
        _clock.Advance(DnsDkimPublicKeyLocator.KeyLifetime);
        await locator.LocatePublicKeyAsync("dns/txt", "example.com", "s1");

        Assert.Equal(2, _asked.Count);
    }

    /// <summary>A dead resolver costs one lookup per selector per minute, not one per mail.</summary>
    [Fact]
    public async Task AMiss_IsKeptForAShorterWhile()
    {
        var locator = Locator();

        await Assert.ThrowsAsync<InvalidOperationException>(() => locator.LocatePublicKeyAsync("dns/txt", "example.com", "s1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => locator.LocatePublicKeyAsync("dns/txt", "example.com", "s1"));
        Assert.Single(_asked);

        _clock.Advance(DnsDkimPublicKeyLocator.MissLifetime);
        await Assert.ThrowsAsync<InvalidOperationException>(() => locator.LocatePublicKeyAsync("dns/txt", "example.com", "s1"));

        Assert.Equal(2, _asked.Count);
    }

    /// <summary>
    /// <b>The case that would otherwise stall the poller</b>: a resolver that never answers and ignores being
    /// cancelled. The lookup is abandoned at its time limit and counts as a miss.
    /// </summary>
    [Fact]
    public async Task ALookupThatNeverAnswers_IsCutOffAndCountsAsAMiss()
    {
        var locator = new DnsDkimPublicKeyLocator((_, _) => new TaskCompletionSource<IReadOnlyList<string>>().Task, _clock, TimeSpan.FromMilliseconds(50));

        var located = locator.LocatePublicKeyAsync("dns/txt", "example.com", "s1");

        Assert.Same(located, await Task.WhenAny(located, Task.Delay(TimeSpan.FromSeconds(10))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => located);
    }

    [Fact]
    public async Task ALookupThatFails_CountsAsAMiss()
    {
        var locator = new DnsDkimPublicKeyLocator((_, _) => throw new TimeoutException("resolver down"), _clock);

        await Assert.ThrowsAsync<InvalidOperationException>(() => locator.LocatePublicKeyAsync("dns/txt", "example.com", "s1"));
    }

    [Fact]
    public async Task ARevokedKey_CountsAsAMiss()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Locator("v=DKIM1; k=rsa; p=").LocatePublicKeyAsync("dns/txt", "example.com", "s1"));
    }

    [Fact]
    public async Task AnUnrelatedRecordBesideTheKey_IsPassedOver()
    {
        var key = await Locator("v=spf1 -all", DkimTestMail.TxtRecord(DkimTestMail.Example)).LocatePublicKeyAsync("dns/txt", "example.com", "s1");

        Assert.Equal(DkimTestMail.Example.Public, key);
    }

    /// <summary>Stopping the application is not a miss, and must not be kept as one.</summary>
    [Fact]
    public async Task TheCallerCancelling_IsPassedOn()
    {
        var locator = new DnsDkimPublicKeyLocator(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return [];
        }, _clock);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => locator.LocatePublicKeyAsync("dns/txt", "example.com", "s1", cancelled.Token));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
