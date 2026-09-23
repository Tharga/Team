using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.MongoDB;

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
    private readonly List<(BsonDocument Filter, BsonDocument Update, EMode? Mode)> _writes = [];
    private readonly ITeamRepositoryCollection<TeamRepositoryConsentTests.TestTeamEntity, TeamRepositoryConsentTests.TestMember> _collection =
        Substitute.For<ITeamRepositoryCollection<TeamRepositoryConsentTests.TestTeamEntity, TeamRepositoryConsentTests.TestMember>>();

    public TeamRepositoryAccessRequestTests()
    {
        var args = new RenderArgs<TeamRepositoryConsentTests.TestTeamEntity>(
            BsonSerializer.LookupSerializer<TeamRepositoryConsentTests.TestTeamEntity>(), BsonSerializer.SerializerRegistry);

        _collection.UpdateOneAsync(
                Arg.Do<FilterDefinition<TeamRepositoryConsentTests.TestTeamEntity>>(f => _writes.Add((f.Render(args), null, null))),
                Arg.Do<UpdateDefinition<TeamRepositoryConsentTests.TestTeamEntity>>(u => _writes[^1] = (_writes[^1].Filter, u.Render(args).AsBsonDocument, _writes[^1].Mode)),
                Arg.Do<OneOption<TeamRepositoryConsentTests.TestTeamEntity>>(o => _writes[^1] = (_writes[^1].Filter, _writes[^1].Update, o?.Mode)));
    }

    private TeamRepository<TeamRepositoryConsentTests.TestTeamEntity, TeamRepositoryConsentTests.TestMember> Sut() => new(_collection);

    private static readonly DateTime At = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Deciding_IsConditionalOnTheRequestStillBeingPending()
    {
        var matched = await Sut().DecideAccessRequestAsync("T1", "r1", TeamAccessRequestStatus.Denied, "owner", At);

        Assert.False(matched);
        var (filter, update, _) = Assert.Single(_writes);
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

        var (filter, update, _) = Assert.Single(_writes);
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

        var (_, update, _) = Assert.Single(_writes);
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
        var (cancelFilter, cancelUpdate, _) = _writes[0];
        Assert.Equal("dev", cancelFilter["AccessRequests"]["$elemMatch"]["RequesterKey"].AsString);
        Assert.Equal("Cancelled", cancelUpdate["$set"]["AccessRequests.$.Status"].AsString);

        var push = _writes[^1].Update["$push"]["AccessRequests"].AsBsonDocument;
        Assert.Equal("r2", push["$each"][0]["_id"].AsString);
        Assert.Equal("User", push["$each"][0]["AccessLevel"].AsString);
        Assert.Equal(TeamAccessRequestRules.HistoryLimit, push["$slice"].AsInt32);
        Assert.Equal(-1, push["$sort"]["RequestedAt"].AsInt32);
    }

    /// <summary>
    /// Every positional write asks for <see cref="EMode.FirstOrDefault"/>, which is the only mode that sends the
    /// caller's filter as the update's own filter.
    /// </summary>
    /// <remarks>
    /// <b>Rendering the right filter is not enough, and that is what this pins.</b> The other modes find the document
    /// first and then update it by <c>_id</c> alone, so the array condition never reaches the update: the positional
    /// <c>$</c> has nothing to resolve and the server rejects the whole command with <i>"The positional operator did
    /// not find the match needed from the query"</i>. Withdrawing a request failed this way in the sample on
    /// 2026-09-22 while every shape assertion above was green — the mode is invisible to them, and
    /// <c>options: null</c> is <see cref="EMode.SingleOrDefault"/> even though a constructed
    /// <see cref="OneOption{TEntity}"/> defaults to <see cref="EMode.FirstOrDefault"/>.
    /// <para>
    /// It is also what makes "still pending" atomic: re-checked by the server as it writes, rather than in a separate
    /// read that two managers can both pass.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EveryPositionalWrite_IsAtomic()
    {
        var sut = Sut();
        await sut.DecideAccessRequestAsync("T1", "r1", TeamAccessRequestStatus.Cancelled, "dev", At);
        await sut.ApproveAccessRequestAsync("T1", "r1", "owner", At, At.AddHours(1), ["Developer"], AccessLevel.User, temporaryConsent: null);
        await sut.AddAccessRequestAsync("T1", new TeamAccessRequestEntity
        {
            Id = "r3",
            RequesterKey = "dev",
            AccessLevel = AccessLevel.User,
            RequestedAt = At,
            Status = TeamAccessRequestStatus.Pending
        });

        var positional = _writes
            .Where(w => w.Update != null && w.Update.Contains("$set") && w.Update["$set"].AsBsonDocument.Names.Any(n => n.Contains(".$.")))
            .ToArray();

        Assert.NotEmpty(positional);
        Assert.All(positional, w => Assert.Equal(EMode.FirstOrDefault, w.Mode));
    }
}
