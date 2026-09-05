using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

internal class TeamRepositoryCollection<TTeamEntity, TMember> : DiskRepositoryCollectionBase<TTeamEntity>, ITeamRepositoryCollection<TTeamEntity, TMember>
    where TTeamEntity : TeamEntityBase<TMember>
    where TMember : TeamMemberBase
{
    private readonly string _collectionName;

    public TeamRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<TTeamEntity, ObjectId>> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.TeamCollectionName ?? "Team";
    }

    public override string CollectionName => _collectionName;

    public override IEnumerable<CreateIndexModel<TTeamEntity>> Indices =>
    [
        new(Builders<TTeamEntity>.IndexKeys.Ascending(x => x.Key), new CreateIndexOptions { Unique = true, Name = "Key" }),
        new(Builders<TTeamEntity>.IndexKeys.Combine(
            Builders<TTeamEntity>.IndexKeys.Ascending(x => x.Id),
            Builders<TTeamEntity>.IndexKeys.Ascending("Members.Key")
        ), new CreateIndexOptions { Unique = true, Name = "UniqueTeamMemberKey" }),
        // Resolves a short invitation token to its team, so the link no longer has to carry the team key.
        //
        // Deliberately NOT unique. Most members carry no invitation, so their array entries index as null,
        // and a unique multikey index enforces across documents -- the second team holding a member without
        // an invitation would collide with the first and fail to save. partialFilterExpression does not
        // rescue it either: it filters whole documents, so a team with both invited and ordinary members
        // still indexes the nulls. Uniqueness comes from 128 bits of entropy instead, and the repository
        // refuses an ambiguous match rather than choosing.
        new(Builders<TTeamEntity>.IndexKeys.Ascending("Members.Invitation.InviteKey"),
            new CreateIndexOptions { Name = "TeamMemberInviteKey" })
    ];
}
