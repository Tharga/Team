namespace Tharga.Team;

/// <summary>
/// Everything an <see cref="ITeamEmailSender"/> is given to write one invitation.
/// </summary>
/// <remarks>
/// <b>Carries the team's key as well as its name.</b> A sender choosing the mail's language, branding or
/// sender address per team has to look the team up, and a display name is neither unique nor stable. Reading
/// the team from ambient state instead — the selected team — is right only while every invitation is raised
/// from a page, and stops being right the moment one is raised by a worker, an endpoint or a bulk invite.
/// <para>
/// A record rather than more parameters, so a later addition is a new property rather than another overload.
/// </para>
/// </remarks>
public record TeamInviteMail
{
    /// <summary>Where the invitation is sent.</summary>
    public required string RecipientEmail { get; init; }

    /// <summary>The name the invitation was raised for.</summary>
    public required string RecipientName { get; init; }

    /// <summary>The link that accepts the invitation.</summary>
    public required string InviteLink { get; init; }

    /// <summary>The key of the team the recipient is invited to.</summary>
    public required string TeamKey { get; init; }

    /// <summary>The display name of that team.</summary>
    public required string TeamName { get; init; }
}
