namespace Tharga.Team.Support.Slack;

/// <summary>
/// Posts messages to Slack.
/// </summary>
/// <remarks>
/// Deliberately knows nothing about teams, users or audit entries. Everything in this namespace is
/// about Slack and only Slack, so it can be lifted into a standalone package as a move rather than a
/// rewrite. <c>SlackNamespaceIsolationTests</c> enforces that.
/// </remarks>
public interface ISlackClient
{
    /// <summary>
    /// Posts <paramref name="text"/> to <paramref name="channel"/>.
    /// </summary>
    /// <param name="channel">Channel name (<c>#alerts</c>) or channel id (<c>C0123456789</c>).</param>
    /// <param name="text">Message body. Slack <c>mrkdwn</c> is supported.</param>
    /// <param name="threadId">
    /// The thread to reply into, or <c>null</c> to start a new one. This is Slack's <c>thread_ts</c>, taken
    /// from <see cref="SlackPostResult.MessageId"/> of the post that opened the thread.
    /// </param>
    /// <param name="cancellationToken">Abandons the post; the caller is told it did not happen.</param>
    /// <returns>The outcome. Implementations report failure rather than throwing.</returns>
    Task<SlackPostResult> PostAsync(string channel, string text, string threadId = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// What happened to a post.
/// </summary>
/// <param name="Success">True when Slack accepted the message.</param>
/// <param name="Error">Why it did not, or null on success.</param>
/// <param name="MessageId">
/// Slack's identifier for the posted message (<c>ts</c>), or null when the post failed.
/// </param>
/// <remarks>
/// <b><see cref="MessageId"/> is what makes a thread possible.</b> Slack has no separate thread object: a
/// thread is the <c>ts</c> of its first message, passed back as <c>thread_ts</c> on every reply. So a caller
/// that wants a conversation has to keep this value; a fire-and-forget notification can ignore it.
/// </remarks>
public readonly record struct SlackPostResult(bool Success, string Error, string MessageId = null)
{
    /// <summary>Slack accepted the message.</summary>
    public static SlackPostResult Ok(string messageId = null) => new(true, null, messageId);

    /// <summary>Slack did not accept it, for the stated reason.</summary>
    public static SlackPostResult Failed(string error) => new(false, error);
}
