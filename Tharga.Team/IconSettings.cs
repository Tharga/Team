namespace Tharga.Team;

/// <summary>
/// Runtime-adjustable icon display/behavior settings. Registered as a singleton, so a host can configure
/// initial values at startup (via the platform options) and change them live. Distinct from
/// <see cref="IconOptions"/>, which holds the upload limits.
/// </summary>
public class IconSettings
{
    /// <summary>Whether the built-in Gravatar fallback is used for users without an uploaded icon.</summary>
    public bool GravatarEnabled { get; set; } = true;

    /// <summary>
    /// Gravatar default-image style used when the email has no Gravatar account — one of
    /// <see cref="GravatarStyles.All"/>. Null or blank resolves to <see cref="GravatarStyles.Default"/>.
    /// </summary>
    public string GravatarStyle { get; set; } = GravatarStyles.Default;

    /// <summary>
    /// A generic default image URL shown for users with no uploaded icon (used after Gravatar, or instead
    /// of it when <see cref="GravatarEnabled"/> is false). Null falls back to initials.
    /// </summary>
    public string DefaultUserIconUrl { get; set; }

    /// <summary>Whether users may upload their own icon (the profile "Change picture" action).</summary>
    public bool AllowUserUpload { get; set; } = true;

    /// <summary>Whether administrators (holders of <c>users:manage</c>) may upload an icon for a user.</summary>
    public bool AllowAdminUpload { get; set; } = true;
}
