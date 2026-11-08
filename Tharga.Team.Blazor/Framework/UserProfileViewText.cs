namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>UserProfileView</c> — the signed-in user's own profile page.</summary>
/// <remarks>
/// <b>The Gravatar sentence is built from whole-sentence keys, not fragments.</b> It used to be a head, a link, and
/// a tail chosen by whether the user can upload, which reads correctly in English and cannot be translated: the
/// clauses reorder. The link is therefore a separate line beside the sentence rather than a word inside it, and each
/// variant is one complete sentence, the same call <see cref="DeleteUserDialogText"/> made for plurals.
/// </remarks>
public static class UserProfileViewText
{
    public static readonly TextKey YourName = new("team.userProfile.yourName", "Your name");
    public static readonly TextKey Save = new("team.userProfile.save", "Save");
    public static readonly TextKey Cancel = new("team.userProfile.cancel", "Cancel");
    public static readonly TextKey EditName = new("team.userProfile.editName", "Edit your name");

    /// <summary>Tooltip on a role that comes from the selected team rather than from the person.</summary>
    public static readonly TextKey TeamRoleTooltip = new("team.userProfile.teamRoleTooltip",
        "From the selected team — changes when you switch teams.");

    /// <summary>Caption under the roles, shown when at least one role comes from the selected team.</summary>
    public static readonly TextKey TeamRolesCaption = new("team.userProfile.teamRolesCaption",
        "Blue roles come from the selected team.");

    public static readonly TextKey ChangePicture = new("team.userProfile.changePicture", "Change picture");
    public static readonly TextKey UsingUploadedPicture = new("team.userProfile.usingUploadedPicture", "Using your uploaded picture.");

    /// <summary>Where the picture comes from, when the user can upload their own.</summary>
    public static readonly TextKey GravatarCanUpload = new("team.userProfile.gravatarCanUpload",
        "Your picture comes from Gravatar (based on your email). Upload your own with “Change picture” above, or manage it at Gravatar.");

    /// <summary>Where the picture comes from, when the user cannot upload.</summary>
    public static readonly TextKey GravatarNoUpload = new("team.userProfile.gravatarNoUpload",
        "Your picture comes from Gravatar (based on your email). Register your email at Gravatar to change it.");

    /// <summary>The link to Gravatar, shown after the sentence.</summary>
    public static readonly TextKey GravatarLink = new("team.userProfile.gravatarLink", "Open Gravatar");

    public static readonly TextKey Claims = new("team.userProfile.claims", "Claims");
    public static readonly TextKey CopyAsJson = new("team.userProfile.copyAsJson", "Copy as JSON");
    public static readonly TextKey NotAuthorized = new("team.userProfile.notAuthorized", "Not authorized");
    public static readonly TextKey ProfilePictureTitle = new("team.userProfile.profilePictureTitle", "Profile picture");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        YourName, Save, Cancel, EditName, TeamRoleTooltip, TeamRolesCaption, ChangePicture, UsingUploadedPicture,
        GravatarCanUpload, GravatarNoUpload, GravatarLink, Claims, CopyAsJson, NotAuthorized, ProfilePictureTitle
    ];
}
