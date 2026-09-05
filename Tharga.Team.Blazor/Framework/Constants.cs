namespace Tharga.Team.Blazor.Framework;

internal static class Constants
{
    public const string TeamKeyCookie = TeamClaimTypes.SelectedTeamKey;
    public const string SelectedTeamKeyCookie = "selected_team_id";
    /// <summary>The query parameter newly minted invitation links use. Short because the link is read by people.</summary>
    public const string TeamInviteToken = "tic";

    /// <summary>
    /// The parameter links minted before 3.20 use. Still accepted, and still the key the code is stashed
    /// under while the invitee signs in -- invitations already sent must keep working.
    /// </summary>
    public const string TeamInviteCode = "TeamInviteCode";
    public const string SelectedTeamLocalStorageKey = "SelectedTeam";
}