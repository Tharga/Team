namespace Tharga.Team.Blazor.Features.Authentication;

/// <summary>
/// Where the profile menu's two built-in items navigate to.
/// </summary>
/// <remarks>
/// The toolkit ships <c>UserProfileView</c> and <c>TeamComponent</c> as components and lets the host mount
/// them at whatever route it likes — but the menu items navigated to the literals <c>profile</c> and
/// <c>team</c>, so a host that mounted either page anywhere else got a menu item leading to a 404 it had no
/// way to correct. Host-supplied items never had the problem: they carry their own <c>Href</c>.
/// <para>
/// Pure and static so it is unit-testable — this project has no bUnit, so a rule left inside razor markup
/// cannot be asserted. Mirrors <c>TeamSelectorGate</c> and <c>TeamMenuItemVisibility</c>.
/// </para>
/// </remarks>
internal static class TeamMenuNavigation
{
    /// <summary>Where the profile item goes when the host has not said otherwise.</summary>
    public const string DefaultProfilePath = "profile";

    /// <summary>Where the team item goes when the host has not said otherwise.</summary>
    public const string DefaultTeamPath = "team";

    /// <summary>
    /// The configured route, or the built-in default when the host has not set one.
    /// </summary>
    /// <remarks>
    /// Whitespace counts as unset. A path of <c>" "</c> is a configuration mistake rather than a request to
    /// navigate nowhere, and treating it as one would break the menu in the way this exists to prevent.
    /// </remarks>
    /// <param name="configuredPath">The host's option value, usually null.</param>
    /// <param name="defaultPath">The built-in route to fall back to.</param>
    public static string Resolve(string configuredPath, string defaultPath)
        => string.IsNullOrWhiteSpace(configuredPath) ? defaultPath : configuredPath;
}
