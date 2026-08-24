namespace Tharga.Team;

/// <summary>
/// Projects a support case onto an external system, and carries messages to it.
/// </summary>
/// <remarks>
/// <b>The case is authoritative; a channel is a projection of it.</b> A case exists, and is complete, whether
/// or not it ever reaches a channel — that is what makes "raised on the site and never sent anywhere" an
/// ordinary case rather than a broken one. A channel adds a way for someone outside the application to take
/// part in the conversation.
/// <para>
/// <b>Nothing here is Slack-shaped, deliberately.</b> A Slack thread is identified by the timestamp of its
/// first message; that is a Slack detail and lives behind <see cref="SupportChannelBinding.ExternalId"/>,
/// where a Jira issue key or anything else fits the same slot. A port defined in one channel's vocabulary
/// would make the second adapter implement the first one's idea of a conversation.
/// </para>
/// <para>
/// <b>This models a conversation, and claims nothing more.</b> Slack is a conversation — open a thread, post
/// into it — and that is the whole of this interface. A ticket system is not: it has status, an assignee and
/// workflow transitions, and *following* a ticket means reading its state rather than writing comments at it.
/// If that is wanted it likely needs its own port beside this one. Forcing a ticket workflow through
/// <see cref="PostAsync"/> is how a port stops describing anything.
/// </para>
/// </remarks>
public interface ISupportChannel
{
    /// <summary>Which external system this channel speaks to.</summary>
    SupportChannelType ChannelType { get; }

    /// <summary>
    /// Opens a projection of a case, returning the binding to store — or <c>null</c> when the channel is not
    /// configured or refused it.
    /// </summary>
    /// <remarks>
    /// <b>Returns null rather than throwing when it cannot open.</b> A channel being unreachable must not stop
    /// a case being raised: the case is the record, and somebody reporting a problem should never depend on a
    /// third party being up. An unbound case can be bound later.
    /// </remarks>
    Task<SupportChannelBinding> OpenAsync(SupportCase supportCase, string openingMessage, CancellationToken cancellationToken = default);

    /// <summary>Posts a message into an already-opened projection.</summary>
    /// <returns><c>true</c> when the channel accepted it.</returns>
    Task<bool> PostAsync(SupportChannelBinding binding, SupportMessage message, CancellationToken cancellationToken = default);
}
