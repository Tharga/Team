namespace Tharga.Team;

/// <summary>
/// An external system a support case can be projected onto.
/// </summary>
/// <remarks>
/// <b>Nothing reads or writes a binding yet.</b> The type exists because a case must be able to outlive and
/// entirely bypass any channel, and that is a property of the model rather than of the channel work. See
/// <see cref="SupportChannelBinding"/>.
/// </remarks>
public enum SupportChannelType
{
    Slack,
    Jira
}
