namespace Tharga.Team;

/// <summary>
/// Where the site's <see cref="IconSettings"/> are kept so they survive a restart. The built-in default
/// (<c>MongoIconSettingsStore</c> in <c>Tharga.Team.MongoDB</c>) stores them in the database; a host can
/// replace it to keep them somewhere else.
/// </summary>
/// <remarks>
/// <b>Site-level, not per team.</b> <see cref="IconSettings"/> is one object for the whole application and
/// <see cref="IconSubject"/> carries no team, so there is exactly one stored value and no key to read it by.
/// <para>
/// <b>A host that never saves anything is unaffected.</b> Nothing is stored until somebody changes a setting,
/// and <see cref="LoadAsync"/> returns null for that, which leaves the values configured at startup in force.
/// Once something is saved, the stored value wins at the next start — which is the point of saving it, and the
/// one behaviour change to say out loud in release notes.
/// </para>
/// </remarks>
public interface IIconSettingsStore
{
    /// <summary>The stored settings, or null when nothing has been saved.</summary>
    Task<IconSettingsState> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Replace the stored settings.</summary>
    Task SaveAsync(IconSettingsState settings, CancellationToken cancellationToken = default);
}

/// <summary>
/// The stored form of <see cref="IconSettings"/> — the values a site can change, without the behaviour.
/// </summary>
/// <remarks>
/// Separate from <see cref="IconSettings"/> on purpose: that is a live singleton the icon pipeline reads on
/// every resolve, and persisting it directly would tie what is stored to what happens to be mutable.
/// </remarks>
public sealed record IconSettingsState
{
    /// <inheritdoc cref="IconSettings.GravatarEnabled"/>
    public bool GravatarEnabled { get; init; } = true;

    /// <inheritdoc cref="IconSettings.GravatarStyle"/>
    public string GravatarStyle { get; init; } = GravatarStyles.Default;

    /// <inheritdoc cref="IconSettings.DefaultUserIconUrl"/>
    public string DefaultUserIconUrl { get; init; }

    /// <inheritdoc cref="IconSettings.AllowUserUpload"/>
    public bool AllowUserUpload { get; init; } = true;

    /// <inheritdoc cref="IconSettings.AllowAdminUpload"/>
    public bool AllowAdminUpload { get; init; } = true;

    /// <summary>Reads the current values off a live <see cref="IconSettings"/>.</summary>
    public static IconSettingsState From(IconSettings settings) => new()
    {
        GravatarEnabled = settings.GravatarEnabled,
        GravatarStyle = settings.GravatarStyle,
        DefaultUserIconUrl = settings.DefaultUserIconUrl,
        AllowUserUpload = settings.AllowUserUpload,
        AllowAdminUpload = settings.AllowAdminUpload
    };

    /// <summary>Writes these values onto a live <see cref="IconSettings"/>.</summary>
    public void ApplyTo(IconSettings settings)
    {
        settings.GravatarEnabled = GravatarEnabled;
        settings.GravatarStyle = string.IsNullOrWhiteSpace(GravatarStyle) ? GravatarStyles.Default : GravatarStyle;
        settings.DefaultUserIconUrl = DefaultUserIconUrl;
        settings.AllowUserUpload = AllowUserUpload;
        settings.AllowAdminUpload = AllowAdminUpload;
    }
}
