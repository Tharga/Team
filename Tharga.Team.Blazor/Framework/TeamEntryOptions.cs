namespace Tharga.Team.Blazor.Framework;

/// <summary>Settings for <see cref="ITeamEntryGuard"/>, the host's rule for who may work in a team. Nothing here matters until a guard is registered.</summary>
public sealed class TeamEntryOptions
{
    /// <summary>
    /// Whether a registered guard is asked. Default <c>true</c>. Set it to <c>false</c> to switch the guard off without removing it, for a host whose
    /// guard misbehaves: team entry then works as it did before one was registered.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// What happens when the guard throws. Default <see cref="TeamEntryErrorBehavior.Deny"/>: a guard is a restriction the host chose, and one that cannot
    /// answer must not quietly let everybody in. <see cref="TeamEntryErrorBehavior.Allow"/> favours availability, for a guard that depends on something that
    /// can be down. Either way the error is logged and audited.
    /// </summary>
    public TeamEntryErrorBehavior OnError { get; set; } = TeamEntryErrorBehavior.Deny;

    /// <summary>
    /// How long an allow or a deny is remembered per user, session and team on this instance. Default 30 seconds. <see cref="TimeSpan.Zero"/> asks the guard
    /// every time. A longer time is cheaper and means another instance learns of a change later; <see cref="ITeamEntryGuardState.InvalidateAsync"/> shortens
    /// that. A step-up is never answered from here, since the caller may complete it on another instance; it is only kept to audit it once per window.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>What happens to team entry when <see cref="ITeamEntryGuard"/> throws.</summary>
public enum TeamEntryErrorBehavior
{
    /// <summary>Entry is refused. The default.</summary>
    Deny,

    /// <summary>Entry is allowed, as if no guard were registered.</summary>
    Allow
}
