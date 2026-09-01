namespace Tharga.Team;

/// <summary>
/// A projection of a support case onto an external system — a Slack thread, an email thread.
/// </summary>
/// <remarks>
/// <b>A case may have none.</b> That is the requirement this type exists to make expressible: a case raised
/// on the site is complete and trackable whether or not it ever reaches a channel, and the toolkit's own case
/// is authoritative rather than the channel's.
/// <para>
/// An empty collection is therefore an ordinary state, not an oversight — it is what every case has until a
/// channel is configured, and what a site-only case has forever.
/// </para>
/// </remarks>
public record SupportChannelBinding
{
    public required SupportChannelType ChannelType { get; init; }

    /// <summary>
    /// The identifier in the external system — a Slack <c>thread_ts</c>, the <c>Message-ID</c> of the mail
    /// that opened an email thread.
    /// </summary>
    public required string ExternalId { get; init; }

    /// <summary>
    /// Who this projection converses with, where the channel is addressed rather than shared — the email
    /// address of the person on the other end. <c>null</c> for a channel that has no such thing.
    /// </summary>
    /// <remarks>
    /// <b>A property of the projection, not of the case.</b> A Slack thread is posted into a room that
    /// anybody in it can answer, so there is nobody to name; an email thread has exactly one correspondent,
    /// and a later reply has to be sent back to them rather than to whoever happens to be signed in.
    /// <para>
    /// <b>It is also the trust anchor for inbound mail.</b> A <c>From:</c> header authenticates nobody, so a
    /// mail is only appended to a case when it comes from the address that case already corresponds with.
    /// </para>
    /// </remarks>
    public string Address { get; init; }
}
