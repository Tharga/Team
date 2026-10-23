using Microsoft.Extensions.Options;
using Tharga.Team.Email.Mailbox;

namespace Tharga.Team.Email.Tests.Mailbox;

public class TeamMailReceiverTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryTeamMailKeyStore _keys = new();
    private readonly InMemoryTeamMailStore _mail = new();
    private readonly InMemoryContentStore _content = new();
    private readonly TeamMailNotifier _notifier = new();
    private readonly List<TeamMailUpdatedEventArgs> _notified = [];

    public TeamMailReceiverTests()
    {
        _keys.Add(new TeamMailKeyRecord { TeamKey = "team-a", Key = "primarykey01", Role = MailKeyRole.Primary, CreatedAt = Now.AddDays(-10) });
        _keys.Add(new TeamMailKeyRecord { TeamKey = "team-a", Key = "secondary001", Role = MailKeyRole.Secondary, CreatedAt = Now.AddDays(-100), RetiresAt = Now.AddDays(30) });
        _notifier.MailUpdated += (_, e) => _notified.Add(e);
    }

    private TeamMailReceiver Receiver() => new(_keys, _mail, _content, _notifier, Options.Create(new TeamMailboxOptions { Domain = "fortdocs.app", Suffix = "-case" }), new SteppingTimeProvider(Now));

    private static InboundMail Mail(string deliveredTo, string messageId = "<m1@example.org>") => new(
        MessageId: messageId,
        From: "anna@example.org",
        DeliveredTo: [deliveredTo],
        Subject: "Begäran om allmän handling",
        Body: "Hej",
        SentAt: new DateTimeOffset(Now.AddMinutes(-5)),
        Uid: 7,
        Raw: [1, 2, 3],
        FromName: "Anna Andersson",
        To: ["primarykey01-case@fortdocs.app"],
        Cc: ["someone@example.org"]);

    [Fact]
    public async Task MailToATeamsAddress_IsKeptForThatTeam_WithItsEnvelope()
    {
        Assert.True(await Receiver().ReceiveAsync(Mail("primarykey01-case+case42@fortdocs.app"), CancellationToken.None));

        var item = Assert.Single(_mail.All);
        Assert.Equal("team-a", item.TeamKey);
        Assert.Equal("case42", item.ItemKey);
        Assert.Equal(MailRouteReason.PrimaryKey, item.Reason);
        Assert.Equal(Now, item.ReceivedAt);
        Assert.Equal("Anna Andersson", item.Envelope.FromName);
        Assert.Equal(["someone@example.org"], item.Envelope.Cc);
        Assert.Equal("primarykey01-case+case42@fortdocs.app", item.Envelope.DeliveredTo);
        Assert.Null(item.Outcome);
    }

    [Fact]
    public async Task TheRawMessage_IsKept_UnderTheMailsId()
    {
        await Receiver().ReceiveAsync(Mail("primarykey01-case@fortdocs.app"), CancellationToken.None);

        var item = Assert.Single(_mail.All);
        Assert.Equal([1, 2, 3], await _content.GetAsync(item.Id));
    }

    [Fact]
    public async Task TheSameDeliveryReadTwice_IsKeptOnce_AndAnnouncedOnce()
    {
        await Receiver().ReceiveAsync(Mail("primarykey01-case@fortdocs.app"), CancellationToken.None);

        Assert.False(await Receiver().ReceiveAsync(Mail("primarykey01-case@fortdocs.app"), CancellationToken.None));

        Assert.Single(_mail.All);
        Assert.Single(_notified);
        Assert.Equal(TeamMailChange.Received, _notified[0].Change);
        Assert.Equal("team-a", _notified[0].TeamKey);
        Assert.False(_notified[0].AffectsPostmaster);
    }

    [Fact]
    public async Task OneMailToTwoAddresses_IsTwoDeliveries()
    {
        _keys.Add(new TeamMailKeyRecord { TeamKey = "team-b", Key = "teambkey0001", Role = MailKeyRole.Primary, CreatedAt = Now });

        await Receiver().ReceiveAsync(Mail("primarykey01-case@fortdocs.app"), CancellationToken.None);
        await Receiver().ReceiveAsync(Mail("teambkey0001-case@fortdocs.app"), CancellationToken.None);

        Assert.Equal(["team-a", "team-b"], _mail.All.Select(x => x.TeamKey).Order());
    }

    [Fact]
    public async Task MailToARetiringKey_ReachesTheTeam_MarkedWithTheRetirementDate()
    {
        await Receiver().ReceiveAsync(Mail("secondary001-case@fortdocs.app"), CancellationToken.None);

        var item = Assert.Single(_mail.All);
        Assert.Equal(MailRouteReason.RetiringKey, item.Reason);
        Assert.Equal(Now.AddDays(30), item.KeyRetiresAt);
    }

    [Fact]
    public async Task MailToAnAddressNobodyHolds_IsKeptByThePostmaster()
    {
        await Receiver().ReceiveAsync(Mail("nosuchkey000-case@fortdocs.app"), CancellationToken.None);

        var item = Assert.Single(_mail.All);
        Assert.Null(item.TeamKey);
        Assert.Equal(MailRouteReason.UnknownAddress, item.Reason);
        Assert.Null(_notified.Single().TeamKey);
        Assert.True(_notified.Single().AffectsPostmaster);
    }

    [Fact]
    public async Task WhenTheContentCannotBeKept_NothingIsRecorded()
    {
        _content.FailNextSave = true;

        await Assert.ThrowsAsync<IOException>(() => Receiver().ReceiveAsync(Mail("primarykey01-case@fortdocs.app"), CancellationToken.None));

        Assert.Empty(_mail.All);
        Assert.Empty(_notified);
    }

    [Fact]
    public void AMailsId_DependsOnTheDelivery_NotOnWhenItWasRead()
    {
        var first = InboundMailId.For("<m1@example.org>", "a-case@fortdocs.app", [1]);

        Assert.Equal(first, InboundMailId.For(" <m1@example.org> ", "A-Case@FortDocs.app", [9]));
        Assert.NotEqual(first, InboundMailId.For("<m1@example.org>", "b-case@fortdocs.app", [1]));
        Assert.Matches("^[0-9a-f]{32}$", first);
    }

    [Fact]
    public void AMailWithNoMessageId_IsKnownByItsContent()
    {
        Assert.Equal(InboundMailId.For(null, "a-case@fortdocs.app", [1, 2]), InboundMailId.For("", "other@fortdocs.app", [1, 2]));
        Assert.NotEqual(InboundMailId.For(null, "a-case@fortdocs.app", [1, 2]), InboundMailId.For(null, "a-case@fortdocs.app", [1, 3]));
    }
}
