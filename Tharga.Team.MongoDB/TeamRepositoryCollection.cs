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

    /// <inheritdoc />
    public Task<bool> TransferOwnershipAsync(string teamKey, string currentOwnerKey, string newOwnerKey, CancellationToken cancellationToken = default)
        => ExecuteAsync(nameof(TransferOwnershipAsync), async (collection, _, ct) =>
        {
            var result = await collection.UpdateOneAsync(
                TransferFilter(teamKey, currentOwnerKey, newOwnerKey),
                TransferUpdate(),
                new UpdateOptions { ArrayFilters = TransferArrayFilters(currentOwnerKey, newOwnerKey) },
                ct);

            return (result.MatchedCount > 0, (int)result.MatchedCount);
        }, Operation.Update, cancellationToken, TransferFilter(teamKey, currentOwnerKey, newOwnerKey));

    private const string OwnerIdentifier = "outgoing";
    private const string NewOwnerIdentifier = "incoming";

    /// <summary>The team, with the current owner still the Owner and the new owner a member of it.</summary>
    internal static FilterDefinition<TTeamEntity> TransferFilter(string teamKey, string currentOwnerKey, string newOwnerKey)
    {
        var builder = Builders<TTeamEntity>.Filter;
        return builder.And(
            builder.Eq(x => x.Key, teamKey),
            builder.ElemMatch(x => x.Members, m => m.Key == currentOwnerKey && m.AccessLevel == AccessLevel.Owner),
            builder.ElemMatch(x => x.Members, m => m.Key == newOwnerKey));
    }

    /// <summary>Both levels in one update, so the team is never seen with two owners or none.</summary>
    internal static UpdateDefinition<TTeamEntity> TransferUpdate()
        => new BsonDocumentUpdateDefinition<TTeamEntity>(new BsonDocument("$set", new BsonDocument
        {
            { $"Members.$[{OwnerIdentifier}].AccessLevel", nameof(AccessLevel.Administrator) },
            { $"Members.$[{NewOwnerIdentifier}].AccessLevel", nameof(AccessLevel.Owner) },
        }));

    internal static IEnumerable<ArrayFilterDefinition> TransferArrayFilters(string currentOwnerKey, string newOwnerKey)
        =>
        [
            new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument($"{OwnerIdentifier}.Key", currentOwnerKey)),
            new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument($"{NewOwnerIdentifier}.Key", newOwnerKey)),
        ];

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
        // still indexes the nulls. Uniqueness comes from 72 bits of entropy instead, and the repository
        // refuses an ambiguous match rather than choosing.
        new(Builders<TTeamEntity>.IndexKeys.Ascending("Members.Invitation.InviteKey"),
            new CreateIndexOptions { Name = "TeamMemberInviteKey" })
    ];
}
