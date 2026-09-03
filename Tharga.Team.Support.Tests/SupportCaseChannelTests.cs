using System.Security.Claims;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;
using Tharga.Team.Support.Slack;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Projecting a case onto Slack: what gets posted, what gets stored, and what happens when Slack will not
/// take it.
/// </summary>
/// <remarks>
/// <b>The no-channel test is the important one.</b> Everything the site-only release shipped has to keep
/// working untouched when no Slack channel is configured — that is not a degraded mode, it is the ordinary
/// one for a host that never wanted Slack. A regression there would break consumers who never opted in.
/// </remarks>
public class SupportCaseChannelTests
{
    private const string TeamKey = "acme";
    private const string Channel = "#support";
    private const string ThreadId = "1724500000.000100";

    [Fact]
    public async Task RaisingACase_PostsToSlack_AndStoresTheReturnedThreadId()
    {
        var (service, store, slack) = Build();
        slack.PostAsync(Channel, Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(SlackPostResult.Ok(ThreadId));

        var raised = await service.RaiseCaseAsync(TeamKey, "Cannot sign in", "It says my key expired.");

        var stored = await store.GetCaseAsync(TeamKey, raised.Id);
        var binding = Assert.Single(stored.Bindings);
        Assert.Equal(SupportChannelType.Slack, binding.ChannelType);
        Assert.Equal(ThreadId, binding.ExternalId);
    }

    /// <summary>
    /// A reply must continue the conversation. Posting without the thread id would scatter the case across
    /// the channel as unrelated top-level messages.
    /// </summary>
    [Fact]
    public async Task AReply_GoesIntoTheSameThread_RatherThanANewMessage()
    {
        var (service, _, slack) = Build();
        slack.PostAsync(Channel, Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(SlackPostResult.Ok(ThreadId));
        slack.PostAsync(Channel, Arg.Any<string>(), ThreadId, Arg.Any<CancellationToken>())
            .Returns(SlackPostResult.Ok("1724500000.000200"));

        var raised = await service.RaiseCaseAsync(TeamKey, "Subject", "Body");

        await service.ReplyToCaseAsync(TeamKey, raised.Id, "Any news?");

        await slack.Received(1).PostAsync(Channel, Arg.Is<string>(x => x.Contains("Any news?")), ThreadId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADeliveredMessage_IsRecordedAsSent()
    {
        var (service, store, slack) = Build();
        slack.PostAsync(Channel, Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(SlackPostResult.Ok(ThreadId));

        var raised = await service.RaiseCaseAsync(TeamKey, "Subject", "Body");

        var messages = await store.GetMessagesAsync(TeamKey, raised.Id, null, 50);
        Assert.Equal(SupportMessageDelivery.Sent, messages.Items[0].Delivery);
    }

    /// <summary>
    /// A channel being down must not stop somebody reporting a problem. The case is the record; the channel
    /// is a projection of it.
    /// </summary>
    [Fact]
    public async Task WhenSlackRefuses_TheCaseIsStillRaised_AndTheEntryIsPending()
    {
        var (service, store, slack) = Build();
        slack.PostAsync(Channel, Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(SlackPostResult.Failed("channel_not_found"));

        var raised = await service.RaiseCaseAsync(TeamKey, "Subject", "Body");

        var stored = await store.GetCaseAsync(TeamKey, raised.Id);
        Assert.NotNull(stored);
        Assert.Empty(stored.Bindings);

        var messages = await store.GetMessagesAsync(TeamKey, raised.Id, null, 50);
        Assert.Equal(SupportMessageDelivery.Pending, messages.Items[0].Delivery);
    }

    /// <summary>
    /// The state a retry or a reminder acts on: written, but never confirmed as delivered.
    /// </summary>
    [Fact]
    public async Task AReplyThatSlackRejects_IsRecordedAsFailed()
    {
        var (service, store, slack) = Build();
        slack.PostAsync(Channel, Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(SlackPostResult.Ok(ThreadId));
        slack.PostAsync(Channel, Arg.Any<string>(), ThreadId, Arg.Any<CancellationToken>())
            .Returns(SlackPostResult.Failed("rate_limited"));

        var raised = await service.RaiseCaseAsync(TeamKey, "Subject", "Body");
        await service.ReplyToCaseAsync(TeamKey, raised.Id, "Any news?");

        var messages = await store.GetMessagesAsync(TeamKey, raised.Id, null, 50);
        Assert.Equal(SupportMessageDelivery.Failed, messages.Items[^1].Delivery);
    }

    /// <summary>
    /// The regression guard for everything the site-only release shipped. No channel configured is the
    /// ordinary state for a host that never wanted Slack, not a degraded one.
    /// </summary>
    [Fact]
    public async Task WithNoChannelConfigured_NothingIsPostedAndTheCaseIsUnchanged()
    {
        var store = new InMemorySupportCaseStore();
        var service = BuildService(store);

        var raised = await service.RaiseCaseAsync(TeamKey, "Subject", "Body");
        await service.ReplyToCaseAsync(TeamKey, raised.Id, "Any news?");

        var stored = await store.GetCaseAsync(TeamKey, raised.Id);
        Assert.Empty(stored.Bindings);

        var messages = await store.GetMessagesAsync(TeamKey, raised.Id, null, 50);
        Assert.Equal(2, messages.Items.Length);
        Assert.All(messages.Items, m => Assert.Equal(SupportMessageDelivery.NotApplicable, m.Delivery));
    }

    private static (ISupportCaseService Service, InMemorySupportCaseStore Store, ISlackClient Slack) Build()
    {
        var store = new InMemorySupportCaseStore();
        var slack = Substitute.For<ISlackClient>();

        var options = Options.Create(new SupportCaseOptions { SlackChannel = Channel });
        var channel = new SlackSupportChannel(slack, options);

        return (BuildService(store, channel), store, slack);
    }

    private static ISupportCaseService BuildService(InMemorySupportCaseStore store, params ISupportChannel[] channels)
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
