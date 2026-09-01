namespace Tharga.Team;

/// <summary>
/// One entry in a support case's transcript.
/// </summary>
/// <remarks>
/// <b>The author is recorded twice, and both are needed.</b> <see cref="AuthorIdentity"/> is the stable
/// subject that survives everything and can be matched exactly; <see cref="AuthorName"/> is a snapshot taken
/// when the message was written, so the transcript stays readable after the author is deleted or leaves the
/// team. The audit trail carries the same pair, for the same reason — see <c>AuditEntry.CallerUserIdentity</c>
/// and <c>AuditEntry.CallerIdentity</c>.
/// </remarks>
public record SupportMessage
{
    /// <summary>Position in the case's transcript, from 1. Also the paging cursor.</summary>
    public required int Sequence { get; init; }

    public required SupportMessageKind Kind { get; init; }

    /// <summary>
    /// The author's stable authentication subject, or <c>null</c> for a <see cref="SupportMessageKind.System"/>
    /// entry.
    /// </summary>
    public string AuthorIdentity { get; init; }

    /// <summary>The author's display name as it was when the message was written.</summary>
    public string AuthorName { get; init; }

    public required string Body { get; init; }

    public required DateTime SentAt { get; init; }

    /// <summary>
    /// Whether this entry reached the case's external channel.
    /// </summary>
    /// <remarks>
    /// A message that arrived <i>from</i> a channel is <see cref="SupportMessageDelivery.Sent"/> by
    /// definition — it is already there, and posting it back would echo it.
    /// </remarks>
    public SupportMessageDelivery Delivery { get; init; }

    /// <summary>
    /// The channel this entry arrived from, or <c>null</c> when it was written through the application
    /// itself.
    /// </summary>
    /// <remarks>
    /// <b>Where a message came from is not the same question as who wrote it.</b> <see cref="Kind"/> says
    /// whether a person or the toolkit produced the entry; this says which door it came through, and the two
    /// vary independently.
    /// <para>
    /// <b>It carries how much the attribution can be trusted</b>, which is why it is worth surfacing rather
    /// than logging. An entry written through the application was authored by an authenticated caller. One
    /// from <see cref="SupportChannelType.Email"/> was not: mail is accepted on a sender match, because
    /// nothing about a <c>From:</c> header authenticates anybody. A reader deciding how much weight to put on
    /// a name needs to be able to tell those apart.
    /// </para>
    /// </remarks>
    public SupportChannelType? Source { get; init; }
}
