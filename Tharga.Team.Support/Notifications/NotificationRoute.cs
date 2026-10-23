namespace Tharga.Team.Support.Notifications;

/// <summary>
/// Sends one kind of event to one channel, worded one way.
/// </summary>
/// <remarks>
/// <b>The set of routes is the allowlist.</b> An event no route matches is not sent, so there is no
/// second concept to keep in step — removing a route is how you stop the posts, and that is
/// configuration rather than a code change.
/// <para>
/// <b>Every matching route fires</b>, not just the first. One event can go to two channels worded two
/// ways, which is the point of routing over a flat allowlist. The cost is that a <c>*</c> route
/// alongside a specific one posts twice; that is visible in configuration and easy to correct.
/// </para>
/// </remarks>
public record NotificationRoute
{
    /// <summary>
    /// Which events this route matches, as <c>feature:action</c> — the same shape as a scope.
    /// </summary>
    /// <remarks>
    /// <c>team:create</c> matches exactly, <c>team:*</c> matches every action on teams, and <c>*</c>
    /// matches everything. Case-insensitive.
    /// </remarks>
    public required string Event { get; init; }

    /// <summary>
    /// Channel to post to — <c>#alerts</c> or a channel id. Null falls back to
    /// <see cref="NotificationOptions.DefaultChannel"/>.
    /// </summary>
    /// <remarks>
    /// Null on the built-in routes, deliberately. A default route cannot invent a channel name that
    /// happens to exist in someone's workspace, so the built-ins stay dormant until a host names one.
    /// </remarks>
    public string Channel { get; init; }

    /// <summary>
    /// The message, with <c>{placeholder}</c> substitutions. Null uses a readable default built from
    /// the event itself.
    /// </summary>
    /// <remarks>
    /// Substitutions are <c>{event}</c>, <c>{feature}</c>, <c>{action}</c>, <c>{actor}</c>,
    /// <c>{team}</c> (the key), <c>{team.display}</c> (the name, or the key when there is none),
    /// <c>{time}</c>, <c>{outcome}</c> and <c>{error}</c>. Any other name is looked up
    /// in the entry's metadata, so the audit vocabulary is usable directly: <c>{team.name}</c>,
    /// <c>{member.email}</c>. A name that resolves to nothing renders as empty rather than being left
    /// in the message as a literal brace.
    /// </remarks>
    public string Template { get; init; }

    /// <summary>
    /// Restricts the route to successes (<c>true</c>) or failures (<c>false</c>). Null, the default,
    /// matches both.
    /// </summary>
    /// <remarks>
    /// Exists so a failures channel is configuration rather than code. Without it a route worded for
    /// success also narrates the times the operation threw.
    /// </remarks>
    public bool? Success { get; init; }
}
