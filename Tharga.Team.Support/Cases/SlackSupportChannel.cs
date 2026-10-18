using Microsoft.Extensions.Options;
using Tharga.Team.Support.Slack;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Projects a support case onto a Slack thread.
/// </summary>
/// <remarks>
/// <b>This lives outside the <c>Slack</c> namespace on purpose.</b> That namespace is kept free of
/// <c>Tharga.Team.*</c> types so it can be lifted into a standalone package as a file move rather than a
/// rewrite, and <c>SlackNamespaceIsolationTests</c> enforces it. This class is the bridge — it knows about
/// both sides — so it belongs on the support side of the line, not the transport side.
/// <para>
/// <b>A Slack thread is the timestamp of its first message.</b> There is no thread object to create: opening
/// a projection means posting the opening message and keeping the <c>ts</c> that comes back. That value is
/// the binding's external id, and every later reply passes it as <c>thread_ts</c>.
/// </para>
/// </remarks>
internal sealed class SlackSupportChannel(ISlackClient slackClient, IOptions<SupportCaseOptions> options) : ISupportChannel
{
    public SupportChannelType ChannelType => SupportChannelType.Slack;

    public async Task<SupportChannelBinding> OpenAsync(SupportCase supportCase, string openingMessage, CancellationToken cancellationToken = default)
    {
        var channel = options.Value.SlackChannel;

        // Not configured is the ordinary state for a host that does not use Slack, so it is a quiet no
        // rather than a failure. The case is raised either way.
        if (string.IsNullOrWhiteSpace(channel)) return null;

        var raisedBy = string.IsNullOrWhiteSpace(supportCase.TeamName)
            ? $"_raised by {SlackText.Escape(supportCase.AuthorName)}_"
            : $"_raised by {SlackText.Escape(supportCase.AuthorName)} in {SlackText.Escape(supportCase.TeamName)}_";
        var heading = RepeatsTheMessage(supportCase.Subject, openingMessage) ? "" : $"*{SlackText.Escape(supportCase.Subject)}*\n";
        var text = $"{heading}{SlackText.Escape(openingMessage)}\n{raisedBy}";

        var result = await slackClient.PostAsync(channel, text, cancellationToken: cancellationToken);

        if (!result.Success || string.IsNullOrEmpty(result.MessageId)) return null;

        return new SupportChannelBinding
        {
            ChannelType = SupportChannelType.Slack,
            ExternalId = result.MessageId
        };
    }

    /// <summary>Whether the subject only repeats the message's opening words, as a subject derived from it does.</summary>
    internal static bool RepeatsTheMessage(string subject, string message)
    {
        if (string.IsNullOrWhiteSpace(subject)) return true;

        var opening = Collapse(subject).TrimEnd('…').TrimEnd();
        return Collapse(message).StartsWith(opening, StringComparison.Ordinal);
    }

    private static string Collapse(string text)
        => string.Join(' ', (text ?? string.Empty).Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

    public async Task<bool> PostAsync(SupportChannelBinding binding, SupportMessage message, CancellationToken cancellationToken = default)
        => (await PostWithResultAsync(binding, message, cancellationToken)).Delivered;

    public async Task<SupportChannelPostResult> PostWithResultAsync(SupportChannelBinding binding, SupportMessage message, CancellationToken cancellationToken = default)
    {
        var channel = options.Value.SlackChannel;

        if (string.IsNullOrWhiteSpace(channel)) return SupportChannelPostResult.Failed("No Slack channel is configured.");
        if (binding?.ExternalId == null) return SupportChannelPostResult.Failed("The case has no Slack thread.");

        var text = message.Kind == SupportMessageKind.System
            ? $"_{SlackText.Escape(message.Body)}_"
            : $"*{SlackText.Escape(message.AuthorName)}*: {SlackText.Escape(message.Body)}";

        var result = await slackClient.PostAsync(channel, text, binding.ExternalId, cancellationToken);

        return result.Success ? SupportChannelPostResult.Ok() : SupportChannelPostResult.Failed(result.Error);
    }
}
