using System.Reflection;
using System.Runtime.CompilerServices;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// Secure files in the MongoDB store: the flag, uploader and removal are kept as written and never a reader's withheld view; the sweep's
/// query finds the files due; recording a removal is conditional, so racing sweeps record it once.
/// </summary>
public class SupportSecureAttachmentStoreTests
{
    private static readonly DateTime Cutoff = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly ISupportCaseRepositoryCollection _collection = Substitute.For<ISupportCaseRepositoryCollection>();
    private FilterDefinition<SupportCaseEntity> _sentFilter;
    private UpdateDefinition<SupportCaseEntity> _sentUpdate;

    private static SupportAttachmentEntity File(string id, bool secure = true, DateTime? removedAt = null) => new()
    {
        Id = id,
        FileName = $"{id}.xlsx",
        ContentType = "application/octet-stream",
        Size = 3,
        Secure = secure,
        UploadedBy = secure ? "alice" : null,
        RemovedAt = removedAt
    };

    private static SupportMessageEntity Entry(int sequence, DateTime sentAt, params SupportAttachmentEntity[] files) => new()
    {
        Sequence = sequence,
        Kind = SupportMessageKind.User,
        AuthorIdentity = "alice",
        AuthorName = "Alice",
        Body = "For support only.",
        SentAt = sentAt,
        Attachments = files.Length == 0 ? null : files
    };

    private static SupportCaseEntity Case(string id, params SupportMessageEntity[] messages) => new()
    {
        CaseId = id,
        TeamKey = "acme",
        AuthorIdentity = "alice",
        AuthorName = "Alice",
        Subject = "Export",
        Status = SupportCaseStatus.Open,
        CreatedAt = Cutoff.AddDays(-60),
        Messages = messages
    };

    [Fact]
    public void ASecureFile_KeepsItsFlagUploaderAndRemoval_ThroughStorage()
    {
        var attachment = new SupportAttachment { Id = "a1", FileName = "salaries.xlsx", ContentType = "application/octet-stream", Size = 3, Secure = true, UploadedBy = "alice", RemovedAt = Cutoff };

        var document = MongoSupportCaseStore.ToEntity(Message(attachment)).ToBsonDocument();
        var back = MongoSupportCaseStore.ToMessage(BsonSerializer.Deserialize<SupportMessageEntity>(document));

        Assert.Equal(attachment, Assert.Single(back.Attachments));
        var stored = StoredFile(document);
        Assert.Equal(BsonType.Boolean, stored[nameof(SupportAttachmentEntity.Secure)].BsonType);
        Assert.Equal("alice", stored[nameof(SupportAttachmentEntity.UploadedBy)].AsString);
        Assert.Equal(BsonType.DateTime, stored[nameof(SupportAttachmentEntity.RemovedAt)].BsonType);
    }

    /// <summary>The sweep's sparse index holds only the cases with a secure file, so an ordinary file must store none of the three fields.</summary>
    [Fact]
    public void AnOrdinaryFile_StoresNoneOfTheSecureFields()
    {
        var stored = StoredFile(MongoSupportCaseStore.ToEntity(Message(new SupportAttachment { Id = "a1", FileName = "a.pdf", ContentType = "application/pdf", Size = 1 })).ToBsonDocument());

        Assert.False(stored.Contains(nameof(SupportAttachmentEntity.Secure)));
        Assert.False(stored.Contains(nameof(SupportAttachmentEntity.UploadedBy)));
        Assert.False(stored.Contains(nameof(SupportAttachmentEntity.RemovedAt)));
    }

    [Fact]
    public void AWithheldView_IsNeverStored()
    {
        var withheld = new SupportAttachment { Id = "a1", FileName = "salaries.xlsx", ContentType = "application/octet-stream", Size = 3, Secure = true, UploadedBy = "alice", Withheld = true };

        var document = MongoSupportCaseStore.ToEntity(Message(withheld)).ToBsonDocument();
        var back = Assert.Single(MongoSupportCaseStore.ToMessage(BsonSerializer.Deserialize<SupportMessageEntity>(document)).Attachments);

        Assert.False(StoredFile(document).Contains(nameof(SupportAttachment.Withheld)));
        Assert.False(back.Withheld);
        Assert.Equal("salaries.xlsx", back.FileName);
    }

    [Fact]
    public void TheStore_SaysItKeepsSecureFiles()
    {
        Assert.True(((ISupportCaseStore)new MongoSupportCaseStore(_collection)).KeepsSecureAttachments);
    }

    [Fact]
    public void TheDueQuery_MatchesAnOldEnoughEntry_CarryingASecureFileNotYetRemoved()
    {
        var filter = MongoSupportCaseStore.SecureAttachmentsDue(Cutoff)
            .Render(new RenderArgs<SupportCaseEntity>(BsonSerializer.LookupSerializer<SupportCaseEntity>(), BsonSerializer.SerializerRegistry));

        var entry = filter["Messages"]["$elemMatch"].AsBsonDocument;
        Assert.Equal(new BsonDateTime(Cutoff), entry["SentAt"]["$lt"]);
        var file = entry["Attachments"]["$elemMatch"].AsBsonDocument;
        Assert.True(file["Secure"].AsBoolean);
        Assert.Equal(BsonNull.Value, file["RemovedAt"]);
    }

    [Fact]
    public async Task TheDueFiles_AreTheSecureUnremovedOnes_OnEntriesOldEnough()
    {
        var store = Returning(Case("case-1",
            Entry(1, Cutoff.AddDays(-40), File("due"), File("ordinary", secure: false), File("gone", removedAt: Cutoff.AddDays(-1))),
            Entry(2, Cutoff.AddDays(1), File("young"))));

        var due = await store.GetSecureAttachmentsDueAsync(Cutoff, 10, TestContext.Current.CancellationToken);

        var file = Assert.Single(due);
        Assert.Equal(new SupportAttachmentLocation { TeamKey = "acme", CaseId = "case-1", Sequence = 1, AttachmentId = "due" }, file);
    }

    [Fact]
    public async Task TheDueFiles_StopAtTheLimit()
    {
        var store = Returning(
            Case("case-1", Entry(1, Cutoff.AddDays(-40), File("a"), File("b"))),
            Case("case-2", Entry(1, Cutoff.AddDays(-40), File("c"))));

        var due = await store.GetSecureAttachmentsDueAsync(Cutoff, 2, TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b"], due.Select(x => x.AttachmentId));
    }

    [Fact]
    public async Task RecordingARemoval_IsConditionalOnTheFileStillUnremoved_ByItsPosition()
    {
        var store = Holding(Case("case-1", Entry(1, Cutoff), Entry(2, Cutoff, File("other"), File("a1"))));
        UpdateReturns(Changed(new SupportCaseEntity { CaseId = "case-1", AuthorIdentity = "alice", AuthorName = "Alice", Subject = "Export", Status = SupportCaseStatus.Open, CreatedAt = Cutoff, Messages = [] }));

        Assert.True(await store.TryMarkAttachmentRemovedAsync("case-1", 2, "a1", Cutoff, TestContext.Current.CancellationToken));

        // A file's Id is stored as _id, and the path resolves through the class map to the name the document has.
        var filter = Render(_sentFilter);
        Assert.Equal("case-1", filter["CaseId"].AsString);
        Assert.Equal("a1", filter["Messages.1.Attachments.1._id"].AsString);
        Assert.Equal(BsonNull.Value, filter["Messages.1.Attachments.1.RemovedAt"]);
        var set = _sentUpdate.Render(new RenderArgs<SupportCaseEntity>(BsonSerializer.LookupSerializer<SupportCaseEntity>(), BsonSerializer.SerializerRegistry)).AsBsonDocument["$set"].AsBsonDocument;
        Assert.Equal(new BsonDateTime(Cutoff), set["Messages.1.Attachments.1.RemovedAt"]);
    }

    /// <summary>The second of two racing sweeps matches nothing: no before-image, so it is told it recorded nothing.</summary>
    [Fact]
    public async Task ARemovalAnotherSweepRecordedFirst_ReportsNotRecorded()
    {
        var store = Holding(Case("case-1", Entry(1, Cutoff, File("a1"))));
        UpdateReturns(null);

        Assert.False(await store.TryMarkAttachmentRemovedAsync("case-1", 1, "a1", Cutoff, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFileAlreadyRemoved_OrNotThere_IsNotWrittenAgain()
    {
        var store = Holding(Case("case-1", Entry(1, Cutoff, File("a1", removedAt: Cutoff.AddDays(-1)))));

        Assert.False(await store.TryMarkAttachmentRemovedAsync("case-1", 1, "a1", Cutoff, TestContext.Current.CancellationToken));
        Assert.False(await store.TryMarkAttachmentRemovedAsync("case-1", 1, "missing", Cutoff, TestContext.Current.CancellationToken));
        Assert.False(await store.TryMarkAttachmentRemovedAsync("case-1", 9, "a1", Cutoff, TestContext.Current.CancellationToken));

        await _collection.DidNotReceiveWithAnyArgs().UpdateOneAsync(default(FilterDefinition<SupportCaseEntity>), default, default, default);
    }

    [Fact]
    public void TheSweepsIndex_IsSparse_OnTheSecureFlag()
    {
        var collection = (SupportCaseRepositoryCollection)RuntimeHelpers.GetUninitializedObject(typeof(SupportCaseRepositoryCollection));

        var index = collection.Indices.Single(x => x.Options.Name == "Messages_Attachments_Secure");
        var keys = index.Keys.Render(new RenderArgs<SupportCaseEntity>(BsonSerializer.LookupSerializer<SupportCaseEntity>(), BsonSerializer.SerializerRegistry));

        Assert.Equal(["Messages.Attachments.Secure"], keys.Names);
        Assert.True(index.Options.Sparse);
    }

    private static SupportMessage Message(params SupportAttachment[] attachments) => new()
    {
        Sequence = 2,
        Kind = SupportMessageKind.User,
        Body = "Attached.",
        SentAt = Cutoff,
        Attachments = attachments
    };

    private static BsonDocument StoredFile(BsonDocument message)
        => message[nameof(SupportMessageEntity.Attachments)].AsBsonArray.Single().AsBsonDocument;

    private MongoSupportCaseStore Returning(params SupportCaseEntity[] returned)
    {
        _collection.GetAsync(Arg.Do<FilterDefinition<SupportCaseEntity>>(x => _sentFilter = x)).Returns(returned.ToAsyncEnumerable());

        return new MongoSupportCaseStore(_collection);
    }

    private MongoSupportCaseStore Holding(SupportCaseEntity entity)
    {
        _collection.GetOneAsync(Arg.Any<FilterDefinition<SupportCaseEntity>>(), Arg.Any<OneOption<SupportCaseEntity>>(), Arg.Any<CancellationToken>()).Returns(entity);

        return new MongoSupportCaseStore(_collection);
    }

    private void UpdateReturns(EntityChangeResult<SupportCaseEntity> result)
        => _collection.UpdateOneAsync(
                Arg.Do<FilterDefinition<SupportCaseEntity>>(x => _sentFilter = x),
                Arg.Do<UpdateDefinition<SupportCaseEntity>>(x => _sentUpdate = x),
                Arg.Any<OneOption<SupportCaseEntity>>(),
                Arg.Any<IClientSessionHandle>())
            .Returns(result);

    private static EntityChangeResult<T> Changed<T>(T before)
        => (EntityChangeResult<T>)Activator.CreateInstance(typeof(EntityChangeResult<T>), BindingFlags.NonPublic | BindingFlags.Instance, null, [before, before], null);

    private static BsonDocument Render(FilterDefinition<SupportCaseEntity> filter)
        => filter.Render(new RenderArgs<SupportCaseEntity>(BsonSerializer.LookupSerializer<SupportCaseEntity>(), BsonSerializer.SerializerRegistry));
}
