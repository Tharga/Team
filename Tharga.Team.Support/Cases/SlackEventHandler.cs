using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Support.Slack;

namespace Tharga.Team.Support.Cases;

/// <summary>What the endpoint should do with an inbound Slack request.</summary>
/// <param name="StatusCode">What to answer Slack with.</param>
/// <param name="Body">What to write, if anything — only the setup challenge needs one.</param>
public readonly record struct SlackEventOutcome(int StatusCode, string Body = null)
{
    public static SlackEventOutcome Ok() => new(200);

    public static SlackEventOutcome Challenge(string challenge) => new(200, challenge);

    public static SlackEventOutcome Refused() => new(401);
}

/// <summary>
/// Turns a verified Slack event into a reply on the case its thread belongs to.
/// </summary>
/// <remarks>
/// <b>Separated from the endpoint so the rules can be tested without a web host.</b> Everything that decides
/// whether a request is genuine, already handled, or the toolkit's own echo lives here; the endpoint reads
/// headers and the body and does as it is told.
/// <para>
/// <b>Almost everything answers 200.</b> Slack retries anything else, so an event this application has no
/// interest in — another conversation in a shared channel, a thread it does not know — must be accepted and
/// ignored rather than refused. Only a request that fails verification gets a refusal, because that one is
/// not from Slack at all.
/// </para>
/// <para>
/// <b>Processed inline rather than acked first.</b> The reason to answer before doing the work is that being
/// slow makes Slack retry — but a retry is now idempotent, because <see cref="ISupportEventLedger"/> records
/// the delivery before anything is written. Appending a message is one document update, comfortably inside
/// Slack's three-second budget, so a queue and a pump would add machinery to solve a problem the ledger has
/// already solved.
/// </para>
/// </remarks>
internal sealed class SlackEventHandler(
    ISupportCaseStore store,
    ISupportEventLedger ledger,
    IOptions<SupportCaseOptions> options,
    TimeProvider timeProvider,
    ILogger<SlackEventHandler> logger = null)
{
    private const string Source = "slack";

    public async Task<SlackEventOutcome> HandleAsync(string rawBody, string timestamp, string signature, CancellationToken cancellationToken = default)
    {
        if (!SlackSignatureVerifier.IsValid(options.Value.SigningSecret, timestamp, rawBody, signature, timeProvider.GetUtcNow()))
        {
            logger?.LogWarning("Refused an inbound Slack request that failed signature verification.");
            return SlackEventOutcome.Refused();
        }

        using var document = JsonDocument.Parse(rawBody);
        var root = document.RootElement;

        var type = Text(root, "type");

        // Slack sends this once when event subscriptions are enabled and will not turn them on until the
        // challenge comes back. It is signed like any other request, so it is verified above first.
        if (type == "url_verification") return SlackEventOutcome.Challenge(Text(root, "challenge"));

        if (type != "event_callback") return SlackEventOutcome.Ok();

        // Before any write. A retry carries the same event_id, and the ledger's unique index means exactly
        // one instance proceeds even if two receive it at once.
        if (!await ledger.TryRecordAsync(Source, Text(root, "event_id"), cancellationToken))
        {
            logger?.LogDebug("Ignored a Slack event that had already been handled.");
            return SlackEventOutcome.Ok();
        }

        if (!root.TryGetProperty("event", out var slackEvent)) return SlackEventOutcome.Ok();

        if (Text(slackEvent, "type") != "message") return SlackEventOutcome.Ok();

        // The toolkit posts into this thread itself. Without this it would read its own message back and
        // append it to the case, and every reply would appear twice.
        if (slackEvent.TryGetProperty("bot_id", out _)) return SlackEventOutcome.Ok();
        if (Text(slackEvent, "subtype") == "bot_message") return SlackEventOutcome.Ok();

        var threadId = Text(slackEvent, "thread_ts");
        var body = Text(slackEvent, "text");

        // A top-level message rather than a thread reply, or an empty one. A support channel carries
        // conversations that are none of this application's business.
        if (string.IsNullOrEmpty(threadId) || string.IsNullOrWhiteSpace(body)) return SlackEventOutcome.Ok();

        var supportCase = await store.GetCaseByBindingAsync(SupportChannelType.Slack, threadId, cancellationToken);

        if (supportCase == null) return SlackEventOutcome.Ok();

        var message = new SupportMessage
        {
            Sequence = 0,
            Kind = SupportMessageKind.User,
            AuthorIdentity = Text(slackEvent, "user"),
            AuthorName = Text(slackEvent, "user"),
            Body = body,
            SentAt = timeProvider.GetUtcNow().UtcDateTime,

            // It came from the channel, so it is already there. Posting it back would echo it into the
            // thread it arrived from.
            Delivery = SupportMessageDelivery.Sent
        };

        await store.AppendMessageAsync(supportCase.TeamKey, supportCase.Id, message, cancellationToken);

        return SlackEventOutcome.Ok();
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
