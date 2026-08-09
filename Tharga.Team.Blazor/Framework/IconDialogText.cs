namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>UserIconDialog</c> — self-service profile picture.</summary>
/// <remarks>
/// <b>The intro is two whole sentences, not one assembled from parts.</b> The component chooses between them
/// on whether a real <c>IIconProcessor</c> is registered, because the default no-op one cannot downscale and
/// promising otherwise tells the user something false. Composing that from a shared prefix plus a ternary
/// tail — which is how it was written — cannot be translated: the clauses reorder, and in several languages
/// the tail changes the verb agreement of the head.
/// </remarks>
public static class UserIconDialogText
{
    /// <summary>Placeholder: the upload ceiling in MB.</summary>
    public static readonly TextKey IntroDownscale = new("team.userIcon.introDownscale",
        "Upload an image (up to {0} MB) or provide an image URL. Images are squared and downscaled automatically — the short side is padded, never cropped. This replaces your Gravatar image.");

    /// <summary>Placeholder: the upload ceiling in MB, used twice.</summary>
    public static readonly TextKey IntroNoDownscale = new("team.userIcon.introNoDownscale",
        "Upload an image (up to {0} MB) or provide an image URL. Images larger than {0} MB are rejected. This replaces your Gravatar image.");

    public static readonly TextKey UploadFile = new("team.userIcon.uploadFile", "Upload a file");
    public static readonly TextKey OrImageUrl = new("team.userIcon.orImageUrl", "…or image URL");
    public static readonly TextKey Download = new("team.userIcon.download", "Download");
    public static readonly TextKey UseGravatar = new("team.userIcon.useGravatar", "Use Gravatar");
    public static readonly TextKey UseGravatarTooltip = new("team.userIcon.useGravatarTooltip", "Remove the uploaded image and fall back to Gravatar");
    public static readonly TextKey Close = new("team.userIcon.close", "Close");
    public static readonly TextKey NotifyUpdated = new("team.userIcon.notifyUpdated", "Profile picture updated");
    public static readonly TextKey NotifyReverted = new("team.userIcon.notifyReverted", "Reverted to Gravatar");
    public static readonly TextKey NotifyFailed = new("team.userIcon.notifyFailed", "Update failed");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        IntroDownscale, IntroNoDownscale, UploadFile, OrImageUrl, Download,
        UseGravatar, UseGravatarTooltip, Close, NotifyUpdated, NotifyReverted, NotifyFailed,
    ];
}

/// <summary>Localizable strings rendered by <c>TeamIconDialog</c>.</summary>
/// <remarks>See <see cref="UserIconDialogText"/> for why the intro is two whole sentences.</remarks>
public static class TeamIconDialogText
{
    /// <summary>Placeholder: the upload ceiling in MB.</summary>
    public static readonly TextKey IntroDownscale = new("team.teamIcon.introDownscale",
        "Upload an image (up to {0} MB) or provide an image URL. Images are squared and downscaled automatically — the short side is padded, never cropped.");

    /// <summary>Placeholder: the upload ceiling in MB, used twice.</summary>
    public static readonly TextKey IntroNoDownscale = new("team.teamIcon.introNoDownscale",
        "Upload an image (up to {0} MB) or provide an image URL. Images larger than {0} MB are rejected.");

    public static readonly TextKey UploadFile = new("team.teamIcon.uploadFile", "Upload a file");
    public static readonly TextKey OrImageUrl = new("team.teamIcon.orImageUrl", "…or image URL");
    public static readonly TextKey Download = new("team.teamIcon.download", "Download");
    public static readonly TextKey RemoveIcon = new("team.teamIcon.removeIcon", "Remove icon");
    public static readonly TextKey Close = new("team.teamIcon.close", "Close");
    public static readonly TextKey NotifyUpdated = new("team.teamIcon.notifyUpdated", "Team icon updated");
    public static readonly TextKey NotifyRemoved = new("team.teamIcon.notifyRemoved", "Team icon removed");
    public static readonly TextKey NotifyFailed = new("team.teamIcon.notifyFailed", "Icon update failed");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        IntroDownscale, IntroNoDownscale, UploadFile, OrImageUrl, Download,
        RemoveIcon, Close, NotifyUpdated, NotifyRemoved, NotifyFailed,
    ];
}
