using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>
/// The site's icon settings, as one document.
/// </summary>
/// <remarks>
/// <b>There is exactly one of these</b>, pinned by <see cref="SingletonKey"/> and a unique index on
/// <see cref="Key"/>. Settings are site-level rather than per team, so there is no tenant to key by — and a
/// fixed key means a save replaces rather than accumulates, which a generated id would not.
/// </remarks>
public record IconSettingsEntity : EntityBase
{
    /// <summary>The key every icon-settings document carries, because there is only one.</summary>
    public const string SingletonKey = "site";

    /// <summary>Always <see cref="SingletonKey"/>.</summary>
    public required string Key { get; init; }

    public bool GravatarEnabled { get; init; } = true;

    /// <summary>One of <see cref="GravatarStyles.All"/>, stored as the string Gravatar itself uses.</summary>
    public string GravatarStyle { get; init; } = GravatarStyles.Default;

    public string DefaultUserIconUrl { get; init; }

    public bool AllowUserUpload { get; init; } = true;

    public bool AllowAdminUpload { get; init; } = true;

    /// <summary>When it was last changed (UTC).</summary>
    /// <remarks>
    /// Who changed it is deliberately not here: the audit log is the record of that, as it is for every other
    /// administrative act in the toolkit, and a second half-kept copy on the document would be the one people
    /// read and the one that goes stale.
    /// </remarks>
    public DateTime ChangedUtc { get; init; }
}
