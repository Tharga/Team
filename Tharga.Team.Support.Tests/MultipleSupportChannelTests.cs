using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// A case projected onto more than one channel at once.
/// </summary>
/// <remarks>
/// <b>Two channels is the ordinary configuration, not an exotic one.</b> Email faces the customer and Slack
/// faces support, so both are live together and a case carries a binding for each — which is what
/// <see cref="SupportCase.Bindings"/> being an array has always meant. Until 3.18 the service took a single
/// channel, so configuring both silently used whichever happened to register first: the model and the wiring
/// disagreed, and nothing failed to compile.
/// </remarks>
public class MultipleSupportChannelTests
{
    private const string TeamKey = "acme";

    [Fact]
    public async Task RaisingACase_OpensAProjectionOnEveryChannel()
    {
        var slack = Channel(SupportChannelType.Slack, "slack-thread");
        var email = Channel(SupportChannelType.Email, "message-id");

        var store = new InMemorySupportCaseStore();
        var raised = await Build(store, slack, email).RaiseCaseAsync(TeamKey, "Subject", "Body");

        var stored = await store.GetCaseAsync(TeamKey, raised.Id);

        Assert.Equal(2, stored.Bindings.Length);
        Assert.Contains(stored.Bindings, x => x.ChannelType == SupportChannelType.Slack);
        Assert.Contains(stored.Bindings, x => x.ChannelType == SupportChannelType.Email);
    }

    /// <summary>
    /// Support answering a case that arrived by mail has to reach the customer's inbox <i>and</i> the Slack
    /// thread support is reading.
    /// </summary>
    [Fact]
    public async Task AReply_IsPostedIntoEveryProjection()
    {
        var slack = Channel(SupportChannelType.Slack, "slack-thread");
        var email = Channel(SupportChannelType.Email, "message-id");

        var store = new InMemorySupportCaseStore();
        var service = Build(store, slack, email);

        var raised = await service.RaiseCaseAsync(TeamKey, "Subject", "Body");
        await service.ReplyToCaseAsync(TeamKey, raised.Id, "Looking into it.");

        // Each channel is given a binding of its own type, never the other's.
        await slack.Received(1).PostAsync(
            Arg.Is<SupportChannelBinding>(x => x.ChannelType == SupportChannelType.Slack),
            Arg.Is<SupportMessage>(x => x.Body == "Looking into it."),
            Arg.Any<CancellationToken>());

        await email.Received(1).PostAsync(
            Arg.Is<SupportChannelBinding>(x => x.ChannelType == SupportChannelType.Email),
            Arg.Is<SupportMessage>(x => x.Body == "Looking into it."),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The asymmetry that made "delivered" mean <i>somewhere</i> rather than <i>everywhere</i>: a case raised
    /// on the site opens a Slack thread for support and opens nothing by mail, because the person who typed
    /// it is already looking at the site.
    /// </summary>
    [Fact]
    public async Task AChannelThatOpensNothing_DoesNotHoldTheEntryAtPending()
    {
        var slack = Channel(SupportChannelType.Slack, "slack-thread");
        var email = Channel(SupportChannelType.Email, externalId: null);

        var store = new InMemorySupportCaseStore();
        var raised = await Build(store, slack, email).RaiseCaseAsync(TeamKey, "Subject", "Body");

        var stored = await store.GetCaseAsync(TeamKey, raised.Id);
        Assert.Single(stored.Bindings);

        var messages = await store.GetMessagesAsync(TeamKey, raised.Id, null, 50);
        Assert.Equal(SupportMessageDelivery.Sent, messages.Items[0].Delivery);
    }

    [Fact]
    public async Task EveryChannelRefusingToOpen_LeavesTheEntryPending()
    {
        var slack = Channel(SupportChannelType.Slack, externalId: null);
        var email = Channel(SupportChannelType.Email, externalId: null);

        var store = new InMemorySupportCaseStore();
        var raised = await Build(store, slack, email).RaiseCaseAsync(TeamKey, "Subject", "Body");

        var stored = await store.GetCaseAsync(TeamKey, raised.Id);
        Assert.Empty(stored.Bindings);

        var messages = await store.GetMessagesAsync(TeamKey, raised.Id, null, 50);
        Assert.Equal(SupportMessageDelivery.Pending, messages.Items[0].Delivery);
    }

    /// <summary>
    /// One channel refusing a post must not mark an entry that did reach somebody as failed.
    /// </summary>
    [Fact]
    public async Task AReplyOneChannelRefuses_IsStillSentWhenAnotherTookIt()
    {
        var slack = Channel(SupportChannelType.Slack, "slack-thread");
        var email = Channel(SupportChannelType.Email, "message-id", postSucceeds: false);

        var store = new InMemorySupportCaseStore();
        var service = Build(store, slack, email);

        var raised = await service.RaiseCaseAsync(TeamKey, "Subject", "Body");
        await service.ReplyToCaseAsync(TeamKey, raised.Id, "Looking into it.");

        var messages = await store.GetMessagesAsync(TeamKey, raised.Id, null, 50);
        Assert.Equal(SupportMessageDelivery.Sent, messages.Items[^1].Delivery);
    }

    [Fact]
    public async Task AReplyEveryChannelRefuses_IsFailed()
    {
        var slack = Channel(SupportChannelType.Slack, "slack-thread", postSucceeds: false);
        var email = Channel(SupportChannelType.Email, "message-id", postSucceeds: false);

        var store = new InMemorySupportCaseStore();
        var service = Build(store, slack, email);

        var raised = await service.RaiseCaseAsync(TeamKey, "Subject", "Body");
        await service.ReplyToCaseAsync(TeamKey, raised.Id, "Looking into it.");

        var messages = await store.GetMessagesAsync(TeamKey, raised.Id, null, 50);
        Assert.Equal(SupportMessageDelivery.Failed, messages.Items[^1].Delivery);
    }

    /// <summary>
    /// A channel with nothing to post into is Pending rather than Failed: nothing was tried, and a
    /// projection may yet open.
    /// </summary>
    [Fact]
    public async Task AReplyWithNoBindingAnywhere_IsPending()
    {
        var slack = Channel(SupportChannelType.Slack, externalId: null);

        var store = new InMemorySupportCaseStore();
        var service = Build(store, slack);

        var raised = await service.RaiseCaseAsync(TeamKey, "Subject", "Body");
        await service.ReplyToCaseAsync(TeamKey, raised.Id, "Any news?");

        var messages = await store.GetMessagesAsync(TeamKey, raised.Id, null, 50);
        Assert.Equal(SupportMessageDelivery.Pending, messages.Items[^1].Delivery);
    }

    /// <param name="externalId">Null makes the channel open nothing, as an unconfigured one does.</param>
    private static ISupportChannel Channel(SupportChannelType type, string externalId = null, bool postSucceeds = true)
    {
        var channel = Substitute.For<ISupportChannel>();

        channel.ChannelType.Returns(type);

        channel.OpenAsync(Arg.Any<SupportCase>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(externalId == null
                ? null
                : new SupportChannelBinding { ChannelType = type, ExternalId = externalId });

        channel.PostAsync(Arg.Any<SupportChannelBinding>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>())
            .Returns(postSucceeds);

        return channel;
    }

    private static ISupportCaseService Build(InMemorySupportCaseStore store, params ISupportChannel[] channels)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "alice"),
            new(ClaimTypes.Name, "Alice"),
            new(TeamClaimTypes.TeamKey, TeamKey)
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

        return new SupportCaseService(store, authorizer, TimeProvider.System, channels);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
