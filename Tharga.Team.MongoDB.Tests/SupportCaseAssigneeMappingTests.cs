using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The member of support staff a case is assigned to is stored with the case and read back, taken away by removing it, and absent from
/// every case nobody was assigned.
/// </summary>
public class SupportCaseAssigneeMappingTests
{
    private static readonly DateTime Raised = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);

    private readonly ISupportCaseRepositoryCollection _collection = Substitute.For<ISupportCaseRepositoryCollection>();

    private static readonly SupportCaseAssignee Sam = new() { Key = "sam-subject", Name = "Sam Support", Email = "sam@example.com" };

    private static SupportCase Case(SupportCaseAssignee assignee = null) => new()
    {
        Id = "case-1",
        TeamKey = "team-a",
        AuthorIdentity = "customer",
        AuthorName = "Cora Customer",
        Subject = "Question",
        Status = SupportCaseStatus.Open,
        CreatedAt = Raised,
        MessageCount = 1,
        Assignee = assignee
    };

    private static SupportMessage First => new()
    {
        Sequence = 1,
        Kind = SupportMessageKind.User,
        AuthorName = "Cora Customer",
        Body = "Hello",
        SentAt = Raised
    };

    private async Task<SupportCaseEntity> StoredAsync(SupportCase supportCase)
    {
        SupportCaseEntity added = null;
        await _collection.AddAsync(Arg.Do<SupportCaseEntity>(x => added = x));

        await new MongoSupportCaseStore(_collection).AddCaseAsync(supportCase, First, TestContext.Current.CancellationToken);

        return added;
    }

    private static BsonDocument Rendered(UpdateDefinition<SupportCaseEntity> update)
        => update.Render(new RenderArgs<SupportCaseEntity>(BsonSerializer.LookupSerializer<SupportCaseEntity>(), BsonSerializer.SerializerRegistry)).AsBsonDocument;

    private async Task<BsonDocument> UpdateIssuedBySettingAsync(SupportCaseAssignee assignee)
    {
        var stored = await StoredAsync(Case());
        _collection.GetOneAsync(Arg.Any<FilterDefinition<SupportCaseEntity>>()).Returns(stored);
        UpdateDefinition<SupportCaseEntity> issued = null;
        await _collection.UpdateOneAsync(Arg.Any<FilterDefinition<SupportCaseEntity>>(), Arg.Do<UpdateDefinition<SupportCaseEntity>>(x => issued = x));

        await new MongoSupportCaseStore(_collection).SetAssigneeAsync("team-a", "case-1", assignee, TestContext.Current.CancellationToken);

        return Rendered(issued);
    }

    [Fact]
    public async Task TheAssignee_IsStoredWithTheCase()
    {
        var stored = await StoredAsync(Case(Sam));

        Assert.Equal("sam-subject", stored.Assignee.Key);
        Assert.Equal("Sam Support", stored.Assignee.Name);
        Assert.Equal("sam@example.com", stored.Assignee.Email);
    }

    [Fact]
    public async Task ACaseWithNoAssignee_StoresNone()
    {
        var document = (await StoredAsync(Case())).ToBsonDocument();

        Assert.False(document.Contains(nameof(SupportCaseEntity.Assignee)));
    }

    [Fact]
    public async Task AnAssigneeWithNoAddress_StoresNoAddress()
    {
        var document = (await StoredAsync(Case(Sam with { Email = null }))).ToBsonDocument();

        Assert.False(document[nameof(SupportCaseEntity.Assignee)].AsBsonDocument.Contains(nameof(SupportCaseAssigneeEntity.Email)));
    }

    [Fact]
    public async Task TheAssignee_IsReadBack()
    {
        var stored = await StoredAsync(Case(Sam));
        _collection.GetOneAsync(Arg.Any<FilterDefinition<SupportCaseEntity>>()).Returns(stored);

        var read = await new MongoSupportCaseStore(_collection).GetCaseByIdAsync("case-1", TestContext.Current.CancellationToken);

        Assert.Equal(Sam, read.Assignee);
    }

    [Fact]
    public async Task ACaseStoredBeforeAssigneesExisted_ReadsNone()
    {
        var stored = await StoredAsync(Case());
        _collection.GetOneAsync(Arg.Any<FilterDefinition<SupportCaseEntity>>()).Returns(stored);

        var read = await new MongoSupportCaseStore(_collection).GetCaseByIdAsync("case-1", TestContext.Current.CancellationToken);

        Assert.Null(read.Assignee);
    }

    [Fact]
    public async Task Assigning_SetsTheAssignee_AndNothingElse()
    {
        var update = await UpdateIssuedBySettingAsync(Sam);

        Assert.Equal(["$set"], update.Names.ToArray());
        Assert.Equal(["Assignee"], update["$set"].AsBsonDocument.Names.ToArray());
        Assert.Equal("sam-subject", update["$set"]["Assignee"]["Key"].AsString);
    }

    [Fact]
    public async Task TakingTheAssigneeAway_RemovesTheField()
    {
        var update = await UpdateIssuedBySettingAsync(null);

        Assert.Equal(["$unset"], update.Names.ToArray());
        Assert.True(update["$unset"].AsBsonDocument.Contains("Assignee"));
    }

    [Fact]
    public async Task AnUnknownCase_IsReported()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new MongoSupportCaseStore(_collection).SetAssigneeAsync("team-a", "no-such-case", Sam, TestContext.Current.CancellationToken));
    }
}
