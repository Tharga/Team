using System.Security.Claims;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Mailbox;
using Tharga.Team.Service;

namespace Tharga.Team.Email.Tests.Mailbox;

public class TeamMailboxMailTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryTeamMailStore _mail = new();
    private readonly InMemoryContentStore _content = new();
    private readonly TeamMailNotifier _notifier = new();
    private readonly ITeamPrincipalAccessor _principal = Substitute.For<ITeamPrincipalAccessor>();

    public TeamMailboxMailTests()
    {
        _principal.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1"), new Claim("sub", "user-1")], "test"))));
    }

    private TeamMailboxService Service(IInboundMailContentStore content = null) =>
        new(new InMemoryTeamMailKeyStore(), _mail, _notifier, _principal, Options.Create(new TeamMailboxOptions { Domain = "fortdocs.app" }), new SteppingTimeProvider(Now), content ?? _content, null);

    private async Task<InboundMailItem> KeepAsync(string id, string teamKey)
    {
        var item = new InboundMailItem
        {
            Id = id,
            TeamKey = teamKey,
            Envelope = new MailEnvelope { MessageId = $"<{id}@example.org>", From = "anna@example.org", DeliveredTo = "key-case@fortdocs.app" },
            Reason = MailRouteReason.PrimaryKey,
            ReceivedAt = Now
        };
        await _mail.AddAsync(item);
        await _content.SaveAsync(id, [42]);
        return item;
    }

    [Fact]
    public async Task ATeamsMail_IsListed_AndCounted_UntilHandled()
    {
        await KeepAsync("mail-1", "team-a");
        await KeepAsync("mail-2", "team-a");

        await Service().SetOutcomeAsync("team-a", "mail-1", "registered", new MailThreadOwner { Type = "case", Id = "42" });

        Assert.Equal(1, await Service().GetUnhandledCountAsync("team-a"));
        Assert.Equal(["mail-2"], (await Service().ListMailAsync("team-a")).Items.Select(x => x.Id));
        Assert.Equal(["mail-1"], (await Service().ListMailAsync("team-a", InboundMailFilter.Handled)).Items.Select(x => x.Id));
    }

    [Fact]
    public async Task HandlingAMail_RecordsWhoAndWhen_AndAnnouncesIt()
    {
        await KeepAsync("mail-1", "team-a");
        var notified = new List<TeamMailUpdatedEventArgs>();
        _notifier.MailUpdated += (_, e) => notified.Add(e);

        var handled = await Service().SetOutcomeAsync("team-a", "mail-1", "registered", new MailThreadOwner { Type = "case", Id = "42" });

        Assert.Equal("user-1", handled.HandledBy);
        Assert.Equal(Now, handled.HandledAt);
        Assert.Equal("42", handled.Owner.Id);
        Assert.Equal(TeamMailChange.Handled, Assert.Single(notified).Change);
    }

    [Fact]
    public async Task ClearingTheOutcome_ReturnsTheMailToTheUnhandled()
    {
        await KeepAsync("mail-1", "team-a");
        await Service().SetOutcomeAsync("team-a", "mail-1", "registered");

        await Service().SetOutcomeAsync("team-a", "mail-1", null);

        Assert.Equal(1, await Service().GetUnhandledCountAsync("team-a"));
    }

    [Fact]
    public async Task AnotherTeamsMail_IsOutOfReach_EvenWithItsId()
    {
        await KeepAsync("mail-1", "team-b");

        Assert.Null(await Service().GetMailAsync("team-a", "mail-1"));
        Assert.Null(await Service().GetContentAsync("team-a", "mail-1"));
        Assert.Null(await Service().SetOutcomeAsync("team-a", "mail-1", "registered"));
    }

    [Fact]
    public async Task MailThePostmasterHolds_IsNoTeamsMail()
    {
        await KeepAsync("mail-1", null);

        Assert.Null(await Service().GetMailAsync(null, "mail-1"));
        Assert.Empty((await Service().ListMailAsync(null)).Items);
    }

    [Fact]
    public async Task AMailsContent_IsTheRawMessage()
    {
        await KeepAsync("mail-1", "team-a");

        var content = await Service().GetContentAsync("team-a", "mail-1");

        Assert.Equal("mail-1", content.MailId);
        Assert.Equal([42], content.Raw);
    }
}
