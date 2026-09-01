using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Turning an inbound Slack event into a reply on a case.
/// </summary>
/// <remarks>
/// <b>Nearly everything answers 200, and that is deliberate.</b> Slack retries anything else, so an event
/// this application has no interest in must be accepted and ignored rather than refused — otherwise a
/// colleague chatting in a shared support channel generates an endless retry loop. Only a request that fails
/// verification is refused, because that one did not come from Slack.
/// </remarks>
public class SlackEventHandlerTests
{
    private const string Secret = "signing-secret";
    private const string TeamKey = "acme";
    private const string ThreadId = "1724500000.000100";

    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AReplyInAKnownThread_IsAppendedToTheCase()
    {
        var (handler, store, supportCase) = await BuildAsync();

        var outcome = await Handle(handler, MessageEvent(ThreadId, "any news?", user: "U123"));

        Assert.Equal(200, outcome.StatusCode);

        var messages = await store.GetMessagesAsync(TeamKey, supportCase.Id, null, 50);
        Assert.Equal("any news?", messages.Items[^1].Body);
        Assert.Equal("U123", messages.Items[^1].AuthorIdentity);
    }

    /// <summary>
    /// A message arriving from the channel is already in the channel; marking it anything else would have the
    /// outbound path post it straight back into the thread it came from.
    /// </summary>
    [Fact]
    public async Task AnInboundReply_IsRecordedAsAlreadyDelivered()
    {
        var (handler, store, supportCase) = await BuildAsync();

        await Handle(handler, MessageEvent(ThreadId, "any news?"));

        var messages = await store.GetMessagesAsync(TeamKey, supportCase.Id, null, 50);
        Assert.Equal(SupportMessageDelivery.Sent, messages.Items[^1].Delivery);
    }

    /// <summary>
    /// A reader cannot otherwise tell a Slack reply from one typed on the site: both are
    /// <see cref="SupportMessageKind.User"/>, and the only hint is that the author name happens to look like
    /// a workspace id.
    /// </summary>
    [Fact]
    public async Task AnInboundReply_RecordsTheChannelItCameFrom()
    {
        var (handler, store, supportCase) = await BuildAsync();

        await Handle(handler, MessageEvent(ThreadId, "any news?"));

        var messages = await store.GetMessagesAsync(TeamKey, supportCase.Id, null, 50);
        Assert.Equal(SupportChannelType.Slack, messages.Items[^1].Source);
    }

    [Fact]
    public async Task AnUnsignedRequest_IsRefusedAndChangesNothing()
    {
        var (handler, store, supportCase) = await BuildAsync();

        var before = (await store.GetMessagesAsync(TeamKey, supportCase.Id, null, 50)).Items.Length;

        var body = MessageEvent(ThreadId, "let me in");
        var outcome = await handler.HandleAsync(body, Unix(Now), "v0=not-a-real-signature");

        Assert.Equal(401, outcome.StatusCode);
        Assert.Equal(before, (await store.GetMessagesAsync(TeamKey, supportCase.Id, null, 50)).Items.Length);
    }

    /// <summary>
    /// Slack will not enable event subscriptions until the challenge comes back, and it is signed like any
    /// other request — so verification runs first.
    /// </summary>
    [Fact]
    public async Task TheSetupChallenge_IsEchoed()
    {
        var (handler, _, _) = await BuildAsync();

        var body = """{"type":"url_verification","challenge":"abc123"}""";

        var outcome = await Handle(handler, body);

        Assert.Equal(200, outcome.StatusCode);
        Assert.Equal("abc123", outcome.Body);
    }

    /// <summary>
    /// Slack retries are guaranteed. Without the ledger the same reply is appended twice, which is the
    /// failure a user actually sees.
    /// </summary>
    [Fact]
    public async Task TheSameEventDeliveredTwice_AppendsOneMessage()
    {
        var (handler, store, supportCase) = await BuildAsync();

        var body = MessageEvent(ThreadId, "any news?", eventId: "Ev123");

        await Handle(handler, body);
        await Handle(handler, body);

        var messages = await store.GetMessagesAsync(TeamKey, supportCase.Id, null, 50);
        Assert.Equal(1, messages.Items.Count(x => x.Body == "any news?"));
    }

    /// <summary>
    /// The toolkit posts into this thread itself. Without this filter it reads its own message back, appends
    /// it, and every reply appears twice.
    /// </summary>
    [Theory]
    [InlineData("""{"type":"event_callback","event_id":"E1","event":{"type":"message","thread_ts":"1724500000.000100","text":"echo","bot_id":"B1"}}""")]
    [InlineData("""{"type":"event_callback","event_id":"E2","event":{"type":"message","thread_ts":"1724500000.000100","text":"echo","subtype":"bot_message"}}""")]
    public async Task TheToolkitsOwnMessages_AreIgnored(string body)
    {
        var (handler, store, supportCase) = await BuildAsync();

        var before = (await store.GetMessagesAsync(TeamKey, supportCase.Id, null, 50)).Items.Length;

        var outcome = await Handle(handler, body);

        Assert.Equal(200, outcome.StatusCode);
        Assert.Equal(before, (await store.GetMessagesAsync(TeamKey, supportCase.Id, null, 50)).Items.Length);
    }

    /// <summary>
    /// A support channel carries conversations that are none of this application's business. Accepted and
    /// ignored — refusing would make Slack retry it forever.
    /// </summary>
    [Fact]
    public async Task AMessageInAnUnknownThread_IsAcceptedAndIgnored()
    {
        var (handler, _, _) = await BuildAsync();

        var outcome = await Handle(handler, MessageEvent("9999999999.999999", "unrelated chatter"));

        Assert.Equal(200, outcome.StatusCode);
    }

    [Fact]
    public async Task ATopLevelMessageWithNoThread_IsIgnored()
    {
        var (handler, _, _) = await BuildAsync();

        var body = """{"type":"event_callback","event_id":"E9","event":{"type":"message","text":"hello channel"}}""";

        Assert.Equal(200, (await Handle(handler, body)).StatusCode);
    }

    private static string MessageEvent(string threadId, string text, string user = "U1", string eventId = "Ev1") =>
        $$$"""
        {"type":"event_callback","event_id":"{{{eventId}}}","event":{"type":"message","thread_ts":"{{{threadId}}}","text":"{{{text}}}","user":"{{{user}}}"}}
        """;

    private static Task<SlackEventOutcome> Handle(SlackEventHandler handler, string body)
        => handler.HandleAsync(body, Unix(Now), Sign(body));

    private static async Task<(SlackEventHandler Handler, InMemorySupportCaseStore Store, SupportCase Case)> BuildAsync()
    {
        var store = new InMemorySupportCaseStore();

        var supportCase = new SupportCase
        {
            Id = "case-1",
            TeamKey = TeamKey,
            AuthorIdentity = "alice",
            AuthorName = "Alice",
            Subject = "Cannot sign in",
            Status = SupportCaseStatus.Open,
            CreatedAt = Now.UtcDateTime,
            MessageCount = 1
        };

        await store.AddCaseAsync(supportCase, new SupportMessage
        {
            Sequence = 1,
            Kind = SupportMessageKind.User,
            AuthorIdentity = "alice",
            AuthorName = "Alice",
            Body = "It says my key expired.",
            SentAt = Now.UtcDateTime
        });

        await store.AddBindingAsync(TeamKey, supportCase.Id, new SupportChannelBinding
        {
            ChannelType = SupportChannelType.Slack,
            ExternalId = ThreadId
        });

        var options = Options.Create(new SupportCaseOptions { SigningSecret = Secret, SlackChannel = "#support" });

        var handler = new SlackEventHandler(store, new InMemoryEventLedger(), options, new FixedTimeProvider(Now));

        return (handler, store, supportCase);
    }

    private static string Unix(DateTimeOffset moment) => moment.ToUnixTimeSeconds().ToString();

    private static string Sign(string body)
    {
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(Secret),
            Encoding.UTF8.GetBytes($"v0:{Unix(Now)}:{body}"));

        return $"v0={Convert.ToHexStringLower(hash)}";
    }

    private sealed class InMemoryEventLedger : ISupportEventLedger
    {
        private readonly HashSet<string> _seen = [];

        public Task<bool> TryRecordAsync(string source, string eventId, CancellationToken cancellationToken = default)
            => Task.FromResult(_seen.Add($"{source}:{eventId}"));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
