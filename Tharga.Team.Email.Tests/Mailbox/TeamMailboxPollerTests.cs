using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Mailbox;

namespace Tharga.Team.Email.Tests.Mailbox;

public class TeamMailboxPollerTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TeamMailboxOptions MailboxOptions = new() { Domain = "fortdocs.app", Suffix = "-case" };

    private readonly InMemoryTeamMailKeyStore _keys = new();
    private readonly InMemoryTeamMailStore _mail = new();
    private readonly InMemoryContentStore _content = new();
    private readonly FakeSource _source = new();
    private readonly InMemoryPositions _positions = new();

    private sealed class FakeSource : IMailboxSource
    {
        public List<InboundMail> Mails { get; } = [];

        public List<MailFetchPosition> AskedFrom { get; } = [];

        public bool CanRead => true;

        public Task<MailFetchResult> FetchAsync(MailFetchPosition position, CancellationToken cancellationToken)
        {
            AskedFrom.Add(position);
            var fresh = Mails.Where(x => x.Uid > position.LastUid).ToArray();
            return Task.FromResult(new MailFetchResult(new MailFetchPosition(1, fresh.Length == 0 ? position.LastUid : fresh.Max(x => x.Uid)), fresh));
        }
    }

    private sealed class InMemoryPositions : ISupportMailPositionStore
    {
        public Dictionary<string, SupportMailPosition> Saved { get; } = [];

        public Task<SupportMailPosition> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(Saved.GetValueOrDefault(key, SupportMailPosition.Start));

        public Task SetAsync(string key, SupportMailPosition position, CancellationToken cancellationToken = default)
        {
            Saved[key] = position;
            return Task.CompletedTask;
        }
    }

    public TeamMailboxPollerTests()
    {
        _keys.Add(new TeamMailKeyRecord { TeamKey = "team-a", Key = "primarykey01", Role = MailKeyRole.Primary, CreatedAt = Now });
    }

    private TeamMailboxPoller Poller()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMailboxSource>(_source);
        services.AddSingleton<ITeamMailKeyStore>(_keys);
        services.AddSingleton<ITeamMailStore>(_mail);
        services.AddSingleton<IInboundMailContentStore>(_content);
        services.AddSingleton<ISupportMailPositionStore>(_positions);
        services.AddSingleton<ITeamMailNotifier, TeamMailNotifier>();
        services.AddSingleton<TimeProvider>(new SteppingTimeProvider(Now));
        services.AddSingleton(Options.Create(MailboxOptions));
        services.AddScoped<TeamMailReceiver>();

        return new TeamMailboxPoller(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), Options.Create(MailboxOptions));
    }

    private static InboundMail Mail(uint uid) => new($"<m{uid}@example.org>", "anna@example.org", ["primarykey01-case@fortdocs.app"], "Hej", "Hej", new DateTimeOffset(Now), Uid: uid, Raw: [(byte)uid]);

    private SupportMailPosition SavedPosition => _positions.Saved[TeamMailboxPoller.PositionKey(MailboxOptions)];

    [Fact]
    public async Task EveryNewMail_IsKept_AndThePositionMovesPastThem()
    {
        _source.Mails.AddRange([Mail(1), Mail(2), Mail(3)]);

        var kept = await Poller().PollAsync(CancellationToken.None);

        Assert.Equal(3, kept);
        Assert.Equal(3u, SavedPosition.LastUid);
    }

    [Fact]
    public async Task TheNextPoll_StartsWhereTheLastOneStopped()
    {
        _source.Mails.AddRange([Mail(1), Mail(2)]);
        var poller = Poller();
        await poller.PollAsync(CancellationToken.None);
        _source.Mails.Add(Mail(3));

        Assert.Equal(1, await poller.PollAsync(CancellationToken.None));
        Assert.Equal(2u, _source.AskedFrom[^1].LastUid);
    }

    [Fact]
    public async Task AMailThatCannotBeKept_HoldsThePosition_SoItIsReadAgain()
    {
        _source.Mails.AddRange([Mail(1), Mail(2), Mail(3)]);
        var poller = Poller();
        await poller.PollAsync(CancellationToken.None);
        _source.Mails.AddRange([Mail(4), Mail(5), Mail(6)]);
        _content.FailNextSave = true;

        var kept = await poller.PollAsync(CancellationToken.None);

        Assert.Equal(0, kept);
        Assert.Equal(3u, SavedPosition.LastUid);

        Assert.Equal(3, await poller.PollAsync(CancellationToken.None));
        Assert.Equal(6, _mail.All.Count);
        Assert.Equal(6u, SavedPosition.LastUid);
    }

    [Fact]
    public async Task AMailThatFailsLaterInTheBatch_KeepsTheOnesBeforeIt_AndStopsJustBeforeIt()
    {
        _source.Mails.AddRange([Mail(1), Mail(2), Mail(3)]);
        _content.FailSaveNumber = 2;
        var poller = Poller();

        Assert.Equal(1, await poller.PollAsync(CancellationToken.None));
        Assert.Equal(1u, SavedPosition.LastUid);

        Assert.Equal(2, await poller.PollAsync(CancellationToken.None));
        Assert.Equal(3, _mail.All.Count);
    }

    [Fact]
    public async Task ASecondInstance_ReadingTheSameMail_KeepsNothingTwice()
    {
        _source.Mails.AddRange([Mail(1), Mail(2)]);

        await Poller().PollAsync(CancellationToken.None);
        _positions.Saved.Clear();
        var second = await Poller().PollAsync(CancellationToken.None);

        Assert.Equal(0, second);
        Assert.Equal(2, _mail.All.Count);
    }

    [Fact]
    public async Task WithoutAContentStore_ThePollerRefusesToStart_AndSaysWhatIsMissing()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var poller = new TeamMailboxPoller(services.GetRequiredService<IServiceScopeFactory>(), Options.Create(MailboxOptions));

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => poller.StartAsync(CancellationToken.None));

        Assert.Contains(nameof(IInboundMailContentStore), refusal.Message);
    }
}
