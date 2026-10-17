using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>
/// MongoDB adapter for <see cref="ISupportSenderDirectory"/>, over the user and team repositories.
/// </summary>
/// <remarks>
/// <b>Reads the repositories, not the services.</b> The registered user and team services are wrapped in
/// authorization decorators that need a signed-in caller, and the mail poller has none. This is framework code
/// in the sense the port describes, so it reads the stores directly.
/// <para>
/// <b>Only a membership that grants access counts.</b> An invitation not yet accepted is no membership at all,
/// and a suspended one is left out because suspension is a containment: mail must not become the way a
/// suspended member still writes into the team. A disabled account is returned with no teams for the same
/// reason, but it is returned — it still holds the address, and an address two accounts hold belongs to
/// neither.
/// </para>
/// </remarks>
internal sealed class MongoSupportSenderDirectory<TUserEntity, TTeamEntity, TMember>(
    IUserRepository<TUserEntity> users,
    ITeamRepository<TTeamEntity, TMember> teams) : ISupportSenderDirectory
    where TUserEntity : EntityBase, IUser
    where TTeamEntity : TeamEntityBase<TMember>
    where TMember : TeamMemberBase
{
    public async Task<SupportSender[]> FindAsync(string address, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address)) return [];

        var senders = new List<SupportSender>();

        foreach (var user in await users.GetByEMailAsync(address.Trim()))
        {
            senders.Add(new SupportSender
            {
                Identity = user.Identity,
                Name = string.IsNullOrWhiteSpace(user.Name) ? null : user.Name,
                Teams = user.DisabledAt == null ? await ActiveTeamsAsync(user.Key, cancellationToken) : []
            });
        }

        return [.. senders];
    }

    private async Task<SupportCaseTeam[]> ActiveTeamsAsync(string userKey, CancellationToken cancellationToken)
    {
        var found = new List<SupportCaseTeam>();

        await foreach (var team in teams.GetTeamsByUserAsync(userKey).WithCancellation(cancellationToken))
        {
            if (team.DeletedAt != null) continue;

            var grantsAccess = (team.Members ?? []).Any(x => x.Key == userKey && x.State == MembershipState.Member && x.SuspendedAt == null);

            if (grantsAccess && found.All(x => x.Key != team.Key)) found.Add(new SupportCaseTeam { Key = team.Key, Name = team.Name });
        }

        return [.. found];
    }
}
