namespace Tharga.Team;

/// <summary>
/// An external system a support case can be projected onto.
/// </summary>
/// <remarks>
/// <b>A case must be able to outlive and entirely bypass every channel here</b>, which is a property of the
/// model rather than of any one channel's work. See <see cref="SupportChannelBinding"/>.
/// <para>
/// <see cref="Slack"/> and <see cref="Email"/> are implemented. <see cref="Jira"/> is not, and probably wants
/// a port of its own rather than <see cref="ISupportChannel"/> — following a ticket means reading status,
/// assignee and workflow, where this models a conversation.
/// </para>
/// <para>
/// <b>Persisted by name, never by ordinal</b>, so adding a member here renumbers nothing and needs no
/// migration.
/// </para>
/// </remarks>
public enum SupportChannelType
{
    Slack,
    Jira,
    Email
}
