using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Whether an answer reached its channel, why not when it did not, and sending it again.
/// </summary>
public class SupportDeliveryStatusTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice";

    [Fact]
    public async Task AnAnswerTheChannelRefuses_KeepsTheChannelsReason()
    {
        var store = new InMemorySupportCaseStore();
        var email = Channel(SupportChannelType.Email, SupportChannelPostResult.Failed("550 5.1.1 No such user"));
        var raised = await Member(store, email).RaiseCaseAsync(TeamA, "Subject", "Body");

        await Staff(store, email).AnswerCaseAsync(TeamA, raised.Id, "Answer.");

        var answer = await Last(store, raised.Id);
        Assert.Equal(SupportMessageDelivery.Failed, answer.Delivery);
        Assert.Equal("550 5.1.1 No such user", answer.DeliveryError);
    }

    [Fact]
    public async Task EveryChannelRefusing_KeepsTheFirstReason()
    {
        var store = new InMemorySupportCaseStore();
        var slack = Channel(SupportChannelType.Slack, SupportChannelPostResult.Failed("channel_not_found"));
        var email = Channel(SupportChannelType.Email, SupportChannelPostResult.Failed("550 5.1.1 No such user"));
        var raised = await Member(store, slack, email).RaiseCaseAsync(TeamA, "Subject", "Body");

        await Staff(store, slack, email).AnswerCaseAsync(TeamA, raised.Id, "Answer.");

        Assert.Equal("channel_not_found", (await Last(store, raised.Id)).DeliveryError);
    }

    [Fact]
    public async Task OneChannelTakingIt_IsSentWithNoReason()
    {
        var store = new InMemorySupportCaseStore();
        var slack = Channel(SupportChannelType.Slack, SupportChannelPostResult.Ok());
        var email = Channel(SupportChannelType.Email, SupportChannelPostResult.Failed("550 5.1.1 No such user"));
        var raised = await Member(store, slack, email).RaiseCaseAsync(TeamA, "Subject", "Body");

        await Staff(store, slack, email).AnswerCaseAsync(TeamA, raised.Id, "Answer.");

        var answer = await Last(store, raised.Id);
        Assert.Equal(SupportMessageDelivery.Sent, answer.Delivery);
        Assert.Null(answer.DeliveryError);
    }

    /// <summary>A host's own channel predates the result type and still works, it just cannot say why.</summary>
    [Fact]
    public async Task AChannelThatOnlyAnswersYesOrNo_StillWorks_WithoutAReason()
    {
        var store = new InMemorySupportCaseStore();
        var channel = new YesOrNoChannel(accepts: false);
        var raised = await Member(store, channel).RaiseCaseAsync(TeamA, "Subject", "Body");

        await Staff(store, channel).AnswerCaseAsync(TeamA, raised.Id, "Answer.");

        var answer = await Last(store, raised.Id);
        Assert.Equal(SupportMessageDelivery.Failed, answer.Delivery);
        Assert.Null(answer.DeliveryError);
        Assert.Equal(1, channel.Posts);
    }

    [Fact]
    public async Task TheDefaultResult_WrapsTheYesOrNoPost()
    {
        ISupportChannel accepting = new YesOrNoChannel(accepts: true);

        var result = await accepting.PostWithResultAsync(new SupportChannelBinding { ChannelType = SupportChannelType.Slack, ExternalId = "1.0" }, Answer(2));

        Assert.True(result.Delivered);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task SendingAFailedAnswerAgain_SendsIt_AndClearsTheReason()
    {
        var store = new InMemorySupportCaseStore();
        var email = Channel(SupportChannelType.Email, SupportChannelPostResult.Failed("Connection refused"));
        var raised = await Member(store, email).RaiseCaseAsync(TeamA, "Subject", "Body");
        var staff = Staff(store, email);
        await staff.AnswerCaseAsync(TeamA, raised.Id, "Answer.");
        var failed = await Last(store, raised.Id);

        email.PostWithResultAsync(Arg.Any<SupportChannelBinding>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>()).Returns(SupportChannelPostResult.Ok());
        var status = await staff.ResendAsync(TeamA, raised.Id, failed.Sequence);

        var resent = await Last(store, raised.Id);
        Assert.Equal(SupportMessageDelivery.Sent, status);
        Assert.Equal(SupportMessageDelivery.Sent, resent.Delivery);
        Assert.Null(resent.DeliveryError);
        await email.Received(2).PostWithResultAsync(Arg.Any<SupportChannelBinding>(), Arg.Is<SupportMessage>(x => x.Sequence == failed.Sequence && x.Body == "Answer."), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendingAgainAndFailingAgain_KeepsTheNewReason()
    {
        var store = new InMemorySupportCaseStore();
        var email = Channel(SupportChannelType.Email, SupportChannelPostResult.Failed("Connection refused"));
        var raised = await Member(store, email).RaiseCaseAsync(TeamA, "Subject", "Body");
        var staff = Staff(store, email);
        await staff.AnswerCaseAsync(TeamA, raised.Id, "Answer.");

        email.PostWithResultAsync(Arg.Any<SupportChannelBinding>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>()).Returns(SupportChannelPostResult.Failed("Mailbox full"));
        var status = await staff.ResendAsync(TeamA, raised.Id, (await Last(store, raised.Id)).Sequence);

        Assert.Equal(SupportMessageDelivery.Failed, status);
        Assert.Equal("Mailbox full", (await Last(store, raised.Id)).DeliveryError);
    }

    [Fact]
    public async Task AnAnswerAlreadySent_IsNotSentTwice()
    {
        var store = new InMemorySupportCaseStore();
        var email = Channel(SupportChannelType.Email, SupportChannelPostResult.Ok());
        var raised = await Member(store, email).RaiseCaseAsync(TeamA, "Subject", "Body");
        var staff = Staff(store, email);
        await staff.AnswerCaseAsync(TeamA, raised.Id, "Answer.");

        var status = await staff.ResendAsync(TeamA, raised.Id, (await Last(store, raised.Id)).Sequence);

        Assert.Equal(SupportMessageDelivery.Sent, status);
        await email.Received(1).PostWithResultAsync(Arg.Any<SupportChannelBinding>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendingAnEntryTheCaseDoesNotHave_IsRefused()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Member(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Staff(store).ResendAsync(TeamA, raised.Id, 42));
    }

    [Fact]
    public async Task SendingAgain_IsAnnouncedAsADeliveryChange()
    {
        var store = new InMemorySupportCaseStore();
        var email = Channel(SupportChannelType.Email, SupportChannelPostResult.Failed("Connection refused"));
        var notifier = Substitute.For<ISupportCaseNotifier>();
        var raised = await Member(store, email).RaiseCaseAsync(TeamA, "Subject", "Body");
        var staff = Staff(store, notifier, email);
        await staff.AnswerCaseAsync(TeamA, raised.Id, "Answer.");

        await staff.ResendAsync(TeamA, raised.Id, (await Last(store, raised.Id)).Sequence);

        notifier.Received(1).Notify(Arg.Is<SupportCaseUpdatedEventArgs>(x => x.CaseId == raised.Id && x.Change == SupportCaseChange.DeliveryChanged));
    }

    /// <summary>Sending again speaks on the channel for support, so it takes what answering takes.</summary>
    [Fact]
    public async Task HavingRaisedTheCase_DoesNotLetYouSendAnAnswerAgain()
    {
        var store = new InMemorySupportCaseStore();
        var email = Channel(SupportChannelType.Email, SupportChannelPostResult.Failed("Connection refused"));
        var alice = Member(store, email);
        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");
        await Staff(store, email).AnswerCaseAsync(TeamA, raised.Id, "Answer.");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => alice.ResendAsync(TeamA, raised.Id, 2));
    }

    [Fact]
    public async Task ATeamManager_MaySendAnAnswerAgain()
    {
        var store = new InMemorySupportCaseStore();
        var email = Channel(SupportChannelType.Email, SupportChannelPostResult.Failed("Connection refused"));
        var raised = await Member(store, email).RaiseCaseAsync(TeamA, "Subject", "Body");
        await Staff(store, email).AnswerCaseAsync(TeamA, raised.Id, "Answer.");

        var status = await Build(store, "bob", null, [email], TeamA, teamScopes: [SupportScopes.Manage]).ResendAsync(TeamA, raised.Id, 2);

        Assert.Equal(SupportMessageDelivery.Failed, status);
    }

    private static async Task<SupportMessage> Last(InMemorySupportCaseStore store, string caseId)
        => (await store.GetMessagesAsync(TeamA, caseId, null, 50)).Items[^1];

    private static SupportMessage Answer(int sequence) => new()
    {
        Sequence = sequence,
        Kind = SupportMessageKind.User,
        AuthorName = "Sam",
        Body = "Answer.",
        SentAt = DateTime.UtcNow,
        AsSupport = true
    };

    /// <summary>A channel that opens a binding and answers posts with <paramref name="result"/>.</summary>
    private static ISupportChannel Channel(SupportChannelType type, SupportChannelPostResult result)
    {
        var channel = Substitute.For<ISupportChannel>();
        channel.ChannelType.Returns(type);
        channel.OpenAsync(Arg.Any<SupportCase>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SupportChannelBinding { ChannelType = type, ExternalId = $"{type}-thread" });
        channel.PostWithResultAsync(Arg.Any<SupportChannelBinding>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>())
            .Returns(result);

        return channel;
    }

    private static ISupportCaseService Member(InMemorySupportCaseStore store, params ISupportChannel[] channels)
        => Build(store, Alice, null, channels, TeamA);

    private static ISupportCaseService Staff(InMemorySupportCaseStore store, params ISupportChannel[] channels)
        => Build(store, "sam", null, channels, systemScopes: [SystemSupportScopes.AllManage]);

    private static ISupportCaseService Staff(InMemorySupportCaseStore store, ISupportCaseNotifier notifier, params ISupportChannel[] channels)
        => Build(store, "sam", notifier, channels, systemScopes: [SystemSupportScopes.AllManage]);

    private static ISupportCaseService Build(InMemorySupportCaseStore store, string subject, ISupportCaseNotifier notifier, ISupportChannel[] channels, string memberOfTeam = null, string[] teamScopes = null, string[] systemScopes = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, subject)
        };

        if (memberOfTeam != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, memberOfTeam));
        claims.AddRange((teamScopes ?? []).Select(s => new Claim(TeamClaimTypes.Scope, s)));
        claims.AddRange((systemScopes ?? []).Select(s => new Claim(TeamClaimTypes.SystemScope, s)));

        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));

        return new AuthorizationSupportCaseServiceDecorator(new SupportCaseService(store, authorizer, TimeProvider.System, channels, notifier), authorizer);
    }

    /// <summary>A channel written before a post could say why, as a host's own would be.</summary>
    private sealed class YesOrNoChannel(bool accepts) : ISupportChannel
    {
        public int Posts { get; private set; }

        public SupportChannelType ChannelType => SupportChannelType.Slack;

        public Task<SupportChannelBinding> OpenAsync(SupportCase supportCase, string openingMessage, CancellationToken cancellationToken = default)
            => Task.FromResult(new SupportChannelBinding { ChannelType = SupportChannelType.Slack, ExternalId = "1.0" });

        public Task<bool> PostAsync(SupportChannelBinding binding, SupportMessage message, CancellationToken cancellationToken = default)
        {
            Posts++;
            return Task.FromResult(accepts);
        }
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
