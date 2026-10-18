using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Mailbox;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Mailbox;

public class PostmasterTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryTeamMailKeyStore _keys = new();
    private readonly InMemoryTeamMailStore _mail = new();
    private readonly InMemoryContentStore _content = new();
    private readonly TeamMailNotifier _notifier = new();
    private readonly ITeamService _teams = Substitute.For<ITeamService>();

    public PostmasterTests()
    {
        _teams.GetTeamByKeyAsync(Arg.Any<string>()).Returns((ITeam)null);
        _teams.GetTeamByKeyAsync("team-a").Returns(Substitute.For<ITeam>());
    }

    private PostmasterService Postmaster() => new(_mail, _notifier, _content, _teams);

    private async Task KeepAsync(string id, string teamKey, DateTime receivedAt, string subject = "Hej")
    {
        await _mail.AddAsync(new InboundMailItem
        {
            Id = id,
            TeamKey = teamKey,
            Envelope = new MailEnvelope { MessageId = $"<{id}@example.org>", From = "anna@example.org", DeliveredTo = "oldkey-case@fortdocs.app", Subject = subject, To = ["oldkey-case@fortdocs.app"] },
            Reason = teamKey == null ? MailRouteReason.RetiredKey : MailRouteReason.PrimaryKey,
            ReceivedAt = receivedAt
        });
        await _content.SaveAsync(id, [7]);
    }

    [Fact]
    public async Task TheListing_ShowsWhoAboutWhatAndWhen_AndNothingMore()
    {
        await KeepAsync("mail-1", null, Now, "Sekretessbelagd uppgift");

        var summary = Assert.Single((await Postmaster().ListAsync()).Items);

        Assert.Equal("mail-1", summary.Id);
        Assert.Equal("anna@example.org", summary.From);
        Assert.Equal("Sekretessbelagd uppgift", summary.Subject);
        Assert.Equal(MailRouteReason.RetiredKey, summary.Reason);
        Assert.Equal(["Id", "From", "Subject", "ReceivedAt", "Reason", "ArchivedAt", "ArchivedBy"], typeof(PostmasterMailSummary).GetProperties().Select(x => x.Name));
    }

    [Fact]
    public async Task OnlyMailNoTeamOwns_IsThePostmasters()
    {
        await KeepAsync("mail-1", null, Now);
        await KeepAsync("mail-2", "team-a", Now);

        Assert.Equal(1, await Postmaster().GetCountAsync());
        Assert.NotNull(await Postmaster().OpenAsync("mail-1"));
        Assert.Null(await Postmaster().OpenAsync("mail-2"));
        Assert.Null(await Postmaster().GetContentAsync("mail-2"));
        Assert.Equal([7], (await Postmaster().GetContentAsync("mail-1")).Raw);
    }

    [Fact]
    public async Task Routing_DeliversTheMailToTheTeam_MarkedAsRerouted_AndAnnouncesIt()
    {
        await KeepAsync("mail-1", null, Now);
        var notified = new List<TeamMailUpdatedEventArgs>();
        _notifier.MailUpdated += (_, e) => notified.Add(e);

        var routed = await Postmaster().RouteAsync("mail-1", "team-a");

        Assert.Equal("team-a", routed.TeamKey);
        Assert.Equal(MailRouteReason.Rerouted, routed.Reason);
        Assert.Equal(0, await Postmaster().GetCountAsync());
        Assert.Equal(TeamMailChange.Rerouted, Assert.Single(notified).Change);
        Assert.Equal("team-a", notified[0].TeamKey);
    }

    [Fact]
    public async Task AMailAlreadyRouted_IsNotRoutedAgain()
    {
        await KeepAsync("mail-1", null, Now);
        await Postmaster().RouteAsync("mail-1", "team-a");

        Assert.Null(await Postmaster().RouteAsync("mail-1", "team-a"));
    }

    [Theory]
    [InlineData("no-such-team")]
    [InlineData("")]
    public async Task ATeamThatDoesNotExist_IsRefused_AndTheMailStays(string teamKey)
    {
        await KeepAsync("mail-1", null, Now);

        await Assert.ThrowsAsync<ArgumentException>(() => Postmaster().RouteAsync("mail-1", teamKey));

        Assert.Equal(1, await Postmaster().GetCountAsync());
    }

    [Fact]
    public async Task TheRetentionSweep_DeletesOldUnroutedMailAndItsContent_AndRecordsEachDeletion()
    {
        await KeepAsync("old", null, Now.AddDays(-91));
        await KeepAsync("recent", null, Now.AddDays(-89));
        await KeepAsync("teams", "team-a", Now.AddDays(-200));
        var recorded = new List<AuditEntry>();
        var auditLogger = Substitute.For<IAuditLogger>();
        auditLogger.When(x => x.Log(Arg.Any<AuditEntry>())).Do(x => recorded.Add(x.Arg<AuditEntry>()));

        var services = new ServiceCollection();
        services.AddSingleton<ITeamMailStore>(_mail);
        services.AddSingleton<IInboundMailContentStore>(_content);
        services.AddSingleton(new CompositeAuditLogger([auditLogger], Options.Create(new AuditOptions())));
        services.AddSingleton<IAuditEntryFactory>(new AuditEntryFactory(new Microsoft.AspNetCore.Http.HttpContextAccessor()));
        var sweep = new PostmasterRetentionSweep(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), Options.Create(new TeamMailboxOptions { Domain = "fortdocs.app" }), new SteppingTimeProvider(Now));

        var deleted = await sweep.SweepAsync(CancellationToken.None);

        Assert.Equal(["old"], deleted);
        Assert.Equal(["recent", "teams"], _mail.All.Select(x => x.Id).Order());
        Assert.Null(await _content.GetAsync("old"));
        var entry = Assert.Single(recorded);
        Assert.Equal(PostmasterRetentionSweep.Action, entry.Action);
        Assert.Equal("old", entry.Metadata[PostmasterRetentionSweep.MailIdKey]);
    }

    [Fact]
    public async Task RemovingATeam_RemovesItsMailItsContentAndItsKeys_AndNoOneElses()
    {
        _keys.Add(new TeamMailKeyRecord { TeamKey = "team-a", Key = "teamakey0001", Role = MailKeyRole.Primary, CreatedAt = Now });
        _keys.Add(new TeamMailKeyRecord { TeamKey = "team-b", Key = "teambkey0001", Role = MailKeyRole.Primary, CreatedAt = Now });
        await KeepAsync("mail-a", "team-a", Now);
        await KeepAsync("mail-b", "team-b", Now);

        var removed = await new TeamMailboxPurgeParticipant(_keys, _mail, _content).PurgeTeamDataAsync("team-a");

        Assert.Equal(1, removed);
        Assert.Equal(["mail-b"], _mail.All.Select(x => x.Id));
        Assert.Null(await _content.GetAsync("mail-a"));
        Assert.Equal(["team-b"], _keys.All.Select(x => x.TeamKey));
    }
}
