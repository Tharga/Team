namespace Tharga.Team;

/// <summary>
/// Reading and changing the site's icon settings. Requires a <i>system</i> <c>users:manage</c> grant.
/// </summary>
/// <remarks>
/// <b>This is where the check lives, not in the component.</b> Changing these values changes how avatars
/// resolve for every user of the application, so a surface that wrote to <see cref="IIconSettingsStore"/>
/// directly would be the only thing standing between any signed-in caller and site-wide behaviour. A
/// component decides what to offer; this decides what is allowed.
/// <para>
/// <c>users:manage</c> rather than a scope of its own: it already authorizes setting a user's icon on their
/// behalf, and the toolkit's rule is that a new scope is warranted when an operation is irreversible or
/// crosses a tenant boundary. This is neither — it is reversible presentation config.
/// </para>
/// <para>
/// Registered with <c>AddSystemService</c>, so <c>ScopeProxy</c> requires the system grant and never consults
/// a team one. No argument names a team, so there is nothing here for a team-bound caller to aim at.
/// </para>
/// </remarks>
public interface IIconSettingsService
{
    /// <summary>The settings currently in force on this instance.</summary>
    [RequireScope(SystemUserScopes.Manage)]
    Task<IconSettingsState> GetAsync();

    /// <summary>
    /// Store <paramref name="settings"/> and apply them to this instance immediately.
    /// </summary>
    /// <remarks>
    /// <b>Applied here as well as stored</b>, so whoever made the change sees it at once rather than waiting
    /// for a refresh — and on a single-instance host that means everywhere. Other instances re-read on
    /// <c>o.Blazor.IconSettingsRefreshInterval</c>.
    /// </remarks>
    [RequireScope(SystemUserScopes.Manage)]
    Task SaveAsync(IconSettingsState settings);
}
