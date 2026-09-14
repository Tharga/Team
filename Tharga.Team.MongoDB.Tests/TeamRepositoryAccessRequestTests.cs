using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The shape of the access-request writes: conditional on the request still being pending, positional so a concurrent
/// request is never overwritten, and — for approval — consent and status in one update.
/// </summary>
/// <remarks>
/// Asserted on the rendered filter and update, which is where the guarantees live. Whether a real server matched is the
/// driver's job; the <c>false</c> path is covered here by a collection that matches nothing.
/// </remarks>
public class TeamRepositoryAccessRequestTests
{
    private readonly List<(BsonDocument Filter, BsonDocument Update)> _writes = [];
    private readonly ITeamRepositoryCollection<TeamRepositoryConsentTests.TestTeamEntity, TeamRepositoryConsentTests.TestMember> _collection =
        Substitute.For<ITeamRepositoryCollection<TeamRepositoryConsentTests.TestTeamEntity, TeamRepositoryConsentTests.TestMember>>();

    public TeamRepositoryAccessRequestTests()
    {
        var args = new RenderArgs<TeamRepositoryConsentTests.TestTeamEntity>(
            BsonSerializer.LookupSerializer<TeamRepositoryConsentTests.TestTeamEntity>(), BsonSerializer.SerializerRegistry);

        _collection.UpdateOneAsync(
                Arg.Do<FilterDefinition<TeamRepositoryConsentTests.TestTeamEntity>>(f => _writes.Add((f.Render(args), null))),
                Arg.Do<UpdateDefinition<TeamRepositoryConsentTests.TestTeamEntity>>(u => _writes[^1] = (_writes[^1].Filter, u.Render(args).AsBsonDocument)));
    }

    private TeamRepository<TeamRepositoryConsentTests.TestTeamEntity, TeamRepositoryConsentTests.TestMember> Sut() => new(_collection);

    private static readonly DateTime At = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Deciding_IsConditionalOnTheRequestStillBeingPending()
    {
        var matched = await Sut().DecideAccessRequestAsync("T1", "r1", TeamAccessRequestStatus.Denied, "owner", At);

        Assert.False(matched);
        var (filter, update) = Assert.Single(_writes);
        var elem = filter["AccessRequests"]["$elemMatch"].AsBsonDocument;
        Assert.Equal("r1", elem["_id"].AsString);
        Assert.Equal("Pending", elem["Status"].AsString);
        Assert.Equal("Denied", update["$set"]["AccessRequests.$.Status"].AsString);
        Assert.Equal("owner", update["$set"]["AccessRequests.$.DecidedBy"].AsString);
    }

    [Fact]
    public async Task Approving_SetsConsentAndStatus_InOneUpdate()
    {
        var temporary = new TemporaryConsentEntity { ExpiresAt = At.AddHours(8), PreviousConsentedRoles = ["Developer"], PreviousConsentAccessLevel = AccessLevel.Viewer };

        await Sut().ApproveAccessRequestAsync("T1", "r1", "owner", At, At.AddHours(8), ["Developer"], AccessLevel.Administrator, temporary);

        var (filter, update) = Assert.Single(_writes);
        Assert.Equal("Pending", filter["AccessRequests"]["$elemMatch"]["Status"].AsString);

        var set = update["$set"].AsBsonDocument;
        Assert.Equal("Approved", set["AccessRequests.$.Status"].AsString);
        Assert.Equal("Administrator", set["ConsentAccessLevel"].AsString);
        Assert.Equal("Developer", set["ConsentedRoles"][0].AsString);
        Assert.Equal("Viewer", set["TemporaryConsent"]["PreviousConsentAccessLevel"].AsString);
        Assert.False(update.Contains("$unset"));
    }

    /// <summary>No end asked for: the consent becomes standing, so any stored window is removed in the same update.</summary>
    [Fact]
    public async Task ApprovingWithNoEnd_RemovesAnyTemporaryConsent()
    {
        await Sut().ApproveAccessRequestAsync("T1", "r1", "owner", At, null, ["Developer"], AccessLevel.User, temporaryConsent: null);

        var (_, update) = Assert.Single(_writes);
        Assert.True(update["$unset"].AsBsonDocument.Contains("TemporaryConsent"));
    }

    [Fact]
    public async Task Adding_PushesNewestFirst_KeepingTheHistoryBounded()
    {
        var request = new TeamAccessRequestEntity
        {
            Id = "r2",
            RequesterKey = "dev",
            AccessLevel = AccessLevel.User,
            RequestedAt = At,
            Status = TeamAccessRequestStatus.Pending
        };

        await Sut().AddAccessRequestAsync("T1", request);

        // The first write cancels the requester's pending requests (none match here), the last pushes.
        var (cancelFilter, cancelUpdate) = _writes[0];
        Assert.Equal("dev", cancelFilter["AccessRequests"]["$elemMatch"]["RequesterKey"].AsString);
        Assert.Equal("Cancelled", cancelUpdate["$set"]["AccessRequests.$.Status"].AsString);

        var push = _writes[^1].Update["$push"]["AccessRequests"].AsBsonDocument;
        Assert.Equal("r2", push["$each"][0]["_id"].AsString);
        Assert.Equal("User", push["$each"][0]["AccessLevel"].AsString);
        Assert.Equal(TeamAccessRequestRules.HistoryLimit, push["$slice"].AsInt32);
        Assert.Equal(-1, push["$sort"]["RequestedAt"].AsInt32);
    }
}
