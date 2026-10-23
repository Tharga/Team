using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

public interface ITeamRepositoryCollection<TTeamEntity, TTeamMemberModel> : IDiskRepositoryCollection<TTeamEntity>
    where TTeamEntity : TeamEntityBase<TTeamMemberModel>
    where TTeamMemberModel : TeamMemberBase
{
    /// <summary>
    /// Promotes one member and demotes another in a single update, only while the demoted one is still the Owner.
    /// False when no team matched.
    /// </summary>
    /// <remarks>
    /// Two members of one array change together, which a positional update cannot express and Tharga.MongoDB does not
    /// pass array filters for, so this goes to the driver directly.
    /// </remarks>
    Task<bool> TransferOwnershipAsync(string teamKey, string currentOwnerKey, string newOwnerKey, CancellationToken cancellationToken = default);
}