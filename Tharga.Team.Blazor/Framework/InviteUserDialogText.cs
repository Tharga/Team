namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>InviteUserDialog</c>.</summary>
/// <remarks>
/// Includes the two validation messages. A form that validates in English inside an otherwise translated
/// dialog is the same defect as an untranslated label — it is simply reached less often, which is why it
/// tends to be the part that gets missed.
/// </remarks>
public static class InviteUserDialogText
{
    public static readonly TextKey Name = new("team.inviteUser.name", "Name");

    public static readonly TextKey NameRequired = new("team.inviteUser.nameRequired", "Name is required.");

    public static readonly TextKey Email = new("team.inviteUser.email", "Email (optional)");

    public static readonly TextKey EmailInvalid = new("team.inviteUser.emailInvalid", "Enter a valid email address.");

    /// <summary>Shown when an email is entered but no <c>ITeamEmailSender</c> is registered.</summary>
    public static readonly TextKey NoEmailSender = new("team.inviteUser.noEmailSender",
        "Email sending is not configured. The invite link will be available for manual sharing.");

    public static readonly TextKey AccessLevel = new("team.inviteUser.accessLevel", "Access Level");

    public static readonly TextKey Submit = new("team.inviteUser.submit", "OK");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Name, NameRequired, Email, EmailInvalid, NoEmailSender, AccessLevel, Submit];
}
