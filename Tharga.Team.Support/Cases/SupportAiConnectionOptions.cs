namespace Tharga.Team.Support.Cases;

/// <summary>
/// Options for letting each team choose what the support assistant answers it with.
/// </summary>
public sealed class SupportAiConnectionOptions
{
    /// <summary>
    /// The team access level granted <see cref="SupportScopes.AssistantManage"/>. Default
    /// <see cref="AccessLevel.Administrator"/>.
    /// </summary>
    /// <remarks>
    /// Null registers the scope but grants it by no access level, so only a role or an explicit override holds it, for
    /// a host where the team's AI account is not every administrator's to change. Changing this changes who is granted,
    /// never what is checked.
    /// </remarks>
    public AccessLevel? ScopeAccessLevel { get; set; } = AccessLevel.Administrator;
}
