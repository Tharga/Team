namespace Tharga.Team;

/// <summary>
/// A projection of a support case onto an external system — a Slack thread, a Jira issue.
/// </summary>
/// <remarks>
/// <b>A case may have none.</b> That is the requirement this type exists to make expressible: a case raised
/// on the site is complete and trackable whether or not it ever reaches Slack, and the toolkit's own case is
/// authoritative rather than the channel's.
/// <para>
/// Modelled now, unused for now. Nothing reads or writes a binding until the channel work lands, so an empty
/// collection is the expected state and not an oversight.
/// </para>
/// </remarks>
public record SupportChannelBinding
{
    public required SupportChannelType ChannelType { get; init; }

    /// <summary>The identifier in the external system — a Slack <c>thread_ts</c>, a Jira issue key.</summary>
    public required string ExternalId { get; init; }
}
