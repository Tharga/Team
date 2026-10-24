namespace Tharga.Team;

/// <summary>
/// Whether an assistant can answer for a team, and if not, why.
/// </summary>
/// <remarks>
/// A reason rather than a flag, so a component can say why the assistant is missing instead of only hiding
/// it: a deployment with no assistant and a team that has not set one up call for different words.
/// <para>
/// <see cref="NotRegistered"/> is the zero value on purpose. A readiness nobody set must never read as ready,
/// or a case would be handed to an assistant that only declines.
/// </para>
/// </remarks>
public enum SupportAssistantReadiness
{
    /// <summary>The deployment has no assistant at all.</summary>
    NotRegistered,

    /// <summary>The deployment has an assistant, but it has nothing to answer with for this team.</summary>
    NotConfigured,

    /// <summary>An assistant can answer for this team.</summary>
    Ready
}
