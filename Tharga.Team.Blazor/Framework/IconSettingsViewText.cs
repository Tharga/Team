namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>IconSettingsView</c>.</summary>
public static class IconSettingsViewText
{
    public static readonly TextKey Title = new("team.iconSettings.title", "Icon display settings");

    public static readonly TextKey Description = new(
        "team.iconSettings.description",
        "How avatars are resolved for everyone using this site. Changes apply immediately here; other instances pick them up within the refresh interval.");

    public static readonly TextKey GravatarEnabled = new(
        "team.iconSettings.gravatarEnabled",
        "Use Gravatar for users without an uploaded icon");

    public static readonly TextKey GravatarStyle = new("team.iconSettings.gravatarStyle", "Gravatar style");

    public static readonly TextKey GravatarStyleHelp = new(
        "team.iconSettings.gravatarStyleHelp",
        "The image Gravatar generates for an email with no Gravatar account of its own.");

    /// <summary>Explains why the preview shows generated images rather than the viewer's own photo.</summary>
    public static readonly TextKey PreviewHelp = new(
        "team.iconSettings.previewHelp",
        "Previewed against your own email, forcing the generated image — so you see the style itself rather than your Gravatar photo.");

    /// <summary>Label under the blank tile, which renders nothing on purpose.</summary>
    public static readonly TextKey BlankPreview = new("team.iconSettings.blankPreview", "No image");

    public static readonly TextKey DefaultUserIconUrl = new("team.iconSettings.defaultUserIconUrl", "Default user icon URL");

    public static readonly TextKey DefaultUserIconUrlHelp = new(
        "team.iconSettings.defaultUserIconUrlHelp",
        "Used when Gravatar is off or produced nothing. Leave blank to fall back to initials.");

    public static readonly TextKey AllowUserUpload = new(
        "team.iconSettings.allowUserUpload",
        "Let users upload their own icon");

    public static readonly TextKey AllowAdminUpload = new(
        "team.iconSettings.allowAdminUpload",
        "Let administrators upload an icon for a user");

    public static readonly TextKey Save = new("team.iconSettings.save", "Save");

    public static readonly TextKey Saved = new("team.iconSettings.saved", "Icon settings saved.");

    public static readonly TextKey Failed = new("team.iconSettings.failed", "Could not save the icon settings.");

    /// <summary>Shown instead of the form to a caller without the system users:manage grant.</summary>
    public static readonly TextKey NotAllowed = new(
        "team.iconSettings.notAllowed",
        "You do not have permission to change the site's icon settings.");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Title, Description, GravatarEnabled, GravatarStyle, GravatarStyleHelp, PreviewHelp, BlankPreview,
        DefaultUserIconUrl, DefaultUserIconUrlHelp, AllowUserUpload, AllowAdminUpload, Save, Saved, Failed, NotAllowed
    ];
}
