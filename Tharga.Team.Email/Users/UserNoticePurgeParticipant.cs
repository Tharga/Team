namespace Tharga.Team.Email.Users;

/// <summary>Removes a team's notice defaults when the team is purged; members' own choices follow the user and stay.</summary>
internal sealed class UserNoticePurgeParticipant(IUserNoticePreferenceStore store) : ITeamPurgeParticipant
{
    public string Name => "notice defaults";

    public Task<int> PurgeTeamDataAsync(string teamKey, CancellationToken cancellationToken = default)
        => store.RemoveTeamDefaultsAsync(teamKey, cancellationToken);
}
