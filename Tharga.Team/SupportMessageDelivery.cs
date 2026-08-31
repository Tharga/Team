namespace Tharga.Team;

/// <summary>
/// Whether a support message reached the case's external channel.
/// </summary>
/// <remarks>
/// <b>Recorded per message rather than per case</b>, because a channel can fail for one message and not the
/// next — a thread opened successfully and then Slack rate-limited, or a reply sent while the network was
/// down. A per-case flag would say the conversation is fine while one entry never arrived.
/// <para>
/// <see cref="Pending"/> is the state that makes retrying and reminding possible: a message written but not
/// yet confirmed sent is exactly the entry somebody needs to be told about.
/// </para>
/// </remarks>
public enum SupportMessageDelivery
{
    /// <summary>The case has no external channel, so there is nothing to deliver to.</summary>
    NotApplicable,

    /// <summary>Written, but not yet confirmed as delivered. Retryable, and worth reminding about.</summary>
    Pending,

    /// <summary>The channel accepted it.</summary>
    Sent,

    /// <summary>The channel refused it or was unreachable. Retryable.</summary>
    Failed
}
