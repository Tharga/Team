namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>TeamMailAddressView</c> — a team's mail address, and changing it.
/// </summary>
public static class TeamMailAddressViewText
{
    public static readonly TextKey Title = new("team.mailbox.address.title", "Mail address");

    public static readonly TextKey Explanation = new("team.mailbox.address.explanation", "Mail sent to this address arrives in the team's mailbox.");

    public static readonly TextKey NoAddress = new("team.mailbox.address.none", "The team has no mail address yet.");

    public static readonly TextKey Issue = new("team.mailbox.address.issue", "Create address");

    public static readonly TextKey Copy = new("team.mailbox.address.copy", "Copy");

    public static readonly TextKey Copied = new("team.mailbox.address.copied", "The address was copied.");

    public static readonly TextKey Previous = new("team.mailbox.address.previous", "Previous address");

    /// <summary>Followed by the date the previous address stops being accepted.</summary>
    public static readonly TextKey RetiresOn = new("team.mailbox.address.retiresOn", "Still accepted until");

    public static readonly TextKey Rotate = new("team.mailbox.address.rotate", "Change address");

    public static readonly TextKey ConfirmRotateTitle = new("team.mailbox.address.confirmRotate.title", "Change the mail address?");

    public static readonly TextKey ConfirmRotateBody = new("team.mailbox.address.confirmRotate.body", "Everyone who writes to the team must be told the new address. The current one keeps working for a while, then mail sent to it no longer reaches the team.");

    public static readonly TextKey ConfirmRotateAccept = new("team.mailbox.address.confirmRotate.accept", "Change address");

    public static readonly TextKey ConfirmRotateCancel = new("team.mailbox.address.confirmRotate.cancel", "Cancel");

    public static readonly TextKey SelectTeam = new("team.mailbox.address.selectTeam", "Select a team to see its mail address.");

    public static readonly TextKey NotPermitted = new("team.mailbox.address.notPermitted", "You do not have access to the team's mail address.");

    public static readonly TextKey[] All =
    [
        Title, Explanation, NoAddress, Issue, Copy, Copied, Previous, RetiresOn, Rotate, ConfirmRotateTitle,
        ConfirmRotateBody, ConfirmRotateAccept, ConfirmRotateCancel, SelectTeam, NotPermitted
    ];
}
