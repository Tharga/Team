using System.Reflection;
using System.Runtime.CompilerServices;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The MongoDB adapters behind mail to users: the queue's claim and outcomes are each one conditional find-and-modify,
/// a choice is one document per user and category, and the address is read from the user record.
/// </summary>
public class UserMailStoreTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    private readonly IUserMailCollection _mail = Substitute.For<IUserMailCollection>();
    private readonly IUserMailBatchCollection _batches = Substitute.For<IUserMailBatchCollection>();
    private readonly IUserMailPreferenceCollection _preferences = Substitute.For<IUserMailPreferenceCollection>();

    private FilterDefinition<UserMailEntity> _filter;
    private UpdateDefinition<UserMailEntity> _update;
    private OneOption<UserMailEntity> _options;

    private static string Render(FilterDefinition<UserMailEntity> filter)
        => filter.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).ToString();

    private static string Render(UpdateDefinition<UserMailEntity> update)
        => update.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).ToString();

    private static EntityChangeResult<T> Changed<T>(T before)
        => (EntityChangeResult<T>)Activator.CreateInstance(typeof(EntityChangeResult<T>), BindingFlags.NonPublic | BindingFlags.Instance, null, [before, before], null);

    private void CaptureUpdate(UserMailEntity before)
    {
        _mail.UpdateOneAsync(
                Arg.Do<FilterDefinition<UserMailEntity>>(x => _filter = x),
                Arg.Do<UpdateDefinition<UserMailEntity>>(x => _update = x),
                Arg.Do<OneOption<UserMailEntity>>(x => _options = x),
                Arg.Any<IClientSessionHandle>())
            .Returns(before == null ? null : Changed(before));
    }

    private static UserMail Mail(string dedup = null) => new()
    {
        RecipientUserKey = "user-1",
        Category = "news",
        Subject = "Hello",
        BodyHtml = "<p>Hi</p>",
        Body = "Hi",
        ReplyTo = "team@example.com",
        DeduplicationKey = dedup,
        Attachments = [new UserMailAttachment { FileName = "a.pdf", ContentType = "application/pdf", Content = [1, 2, 3], Size = 3, ContentId = "logo" }]
    };

    [Fact]
    public void AQueuedMail_SurvivesTheRoundTripToStorage()
    {
        var entity = MongoUserMailOutbox.ToEntity(Mail("d1"), Now);
        var item = MongoUserMailOutbox.ToItem(entity);

        Assert.Equal(entity.Id.ToString(), item.Id);
        Assert.Equivalent(Mail("d1"), item.Mail);
        Assert.Equal(UserMailStatus.Queued, item.Status);
        Assert.Equal(Now, item.QueuedAt);
        Assert.Equal(Now, item.NextAttemptAt);
        Assert.Equal(0, item.Attempts);
    }

    [Fact]
    public void TheStatus_IsStoredByName()
    {
        var document = MongoUserMailOutbox.ToEntity(Mail(), Now).ToBsonDocument();

        Assert.Equal(BsonType.String, document[nameof(UserMailEntity.Status)].BsonType);
        Assert.Equal("Queued", document[nameof(UserMailEntity.Status)].AsString);
    }

    [Fact]
    public void AMailWithoutFiles_StillStoresAnArray_SoTheirContentCanBeRemovedInOneUpdate()
    {
        var document = MongoUserMailOutbox.ToEntity(Mail() with { Attachments = null }, Now).ToBsonDocument();

        Assert.Equal(BsonType.Array, document[nameof(UserMailEntity.Attachments)].BsonType);
    }

    [Fact]
    public async Task ANewMail_IsInsertedQueued()
    {
        UserMailEntity added = null;
        await _mail.AddAsync(Arg.Do<UserMailEntity>(x => added = x));

        var queued = await new MongoUserMailOutbox(_mail, _batches).AddAsync(Mail(), Now);

        Assert.False(queued.Duplicate);
        Assert.Equal(added.Id.ToString(), queued.Id);
        Assert.Equal(UserMailStatus.Queued, added.Status);
    }

    [Fact]
    public async Task AMailWhoseDeduplicationKeyIsTaken_IsNotInserted_AndTheFirstIsNamed()
    {
        var existing = MongoUserMailOutbox.ToEntity(Mail("d1"), Now);
        _mail.GetOneAsync(Arg.Any<System.Linq.Expressions.Expression<Func<UserMailEntity, bool>>>(), Arg.Any<OneOption<UserMailEntity>>(), Arg.Any<CancellationToken>()).Returns(existing);

        var queued = await new MongoUserMailOutbox(_mail, _batches).AddAsync(Mail("d1"), Now);

        Assert.True(queued.Duplicate);
        Assert.Equal(existing.Id.ToString(), queued.Id);
        await _mail.DidNotReceive().AddAsync(Arg.Any<UserMailEntity>());
    }

    [Fact]
    public async Task AClaim_IsOneFindAndModify_OnQueuedDueMailNobodyHolds_OldestDueFirst_CountingTheAttempt()
    {
        var stored = MongoUserMailOutbox.ToEntity(Mail(), Now.AddMinutes(-1)) with { Attempts = 1 };
        CaptureUpdate(stored);

        var claimed = await new MongoUserMailOutbox(_mail, _batches).ClaimNextAsync("instance-a", Now, TimeSpan.FromMinutes(5));

        Assert.Equal(EMode.FirstOrDefault, _options.Mode);
        Assert.Contains("NextAttemptAt", _options.Sort.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).ToString());

        var filter = Render(_filter);
        Assert.Contains("\"Status\" : \"Queued\"", filter);
        Assert.Contains("\"NextAttemptAt\" : { \"$lte\"", filter);
        Assert.Contains("\"ClaimedUntil\" : null", filter);
        Assert.Contains("\"ClaimedUntil\" : { \"$lte\"", filter);

        var update = Render(_update);
        Assert.Contains("\"ClaimedBy\" : \"instance-a\"", update);
        Assert.Contains("\"$inc\" : { \"Attempts\" : 1 }", update);

        Assert.Equal("instance-a", claimed.ClaimedBy);
        Assert.Equal(Now.AddMinutes(5), claimed.ClaimedUntil);
        Assert.Equal(2, claimed.Attempts);
    }

    [Fact]
    public async Task NothingDue_ClaimsNothing()
    {
        CaptureUpdate(null);

        Assert.Null(await new MongoUserMailOutbox(_mail, _batches).ClaimNextAsync("instance-a", Now, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task AnOutcome_IsWrittenOnlyWhileTheClaimantStillHoldsTheMail_AndDropsTheFilesContent()
    {
        var stored = MongoUserMailOutbox.ToEntity(Mail(), Now);
        CaptureUpdate(stored);

        var written = await new MongoUserMailOutbox(_mail, _batches).MarkSentAsync(stored.Id.ToString(), "instance-a", "<m1@example.com>", "anna@example.org", Now);

        Assert.True(written);
        Assert.Equal(EMode.FirstOrDefault, _options.Mode);

        var filter = Render(_filter);
        Assert.Contains(stored.Id.ToString(), filter);
        Assert.Contains("\"Status\" : \"Queued\"", filter);
        Assert.Contains("\"ClaimedBy\" : \"instance-a\"", filter);

        var update = Render(_update);
        Assert.Contains("\"Status\" : \"Sent\"", update);
        Assert.Contains("\"MessageId\" : \"<m1@example.com>\"", update);
        Assert.Contains("\"RecipientAddress\" : \"anna@example.org\"", update);
        Assert.Contains("\"Attachments.$[].Content\" : 1", update);
        Assert.Contains("\"ClaimedBy\" : 1", update);
    }

    [Fact]
    public async Task AnOutcomeForAMailTakenOverByAnother_IsRefused()
    {
        CaptureUpdate(null);
        var outbox = new MongoUserMailOutbox(_mail, _batches);
        var id = ObjectId.GenerateNewId().ToString();

        Assert.False(await outbox.MarkFailedAsync(id, "instance-a", "late", Now));
        Assert.False(await outbox.MarkSkippedAsync(id, "instance-a", "late", Now));
        Assert.False(await outbox.PostponeAsync(id, "instance-a", "late", Now));
        Assert.False(await outbox.MarkSentAsync("not-an-id", "instance-a", "m", "a@example.org", Now));
    }

    [Fact]
    public async Task APostponedMail_StaysQueued_ReleasedForItsNextAttempt()
    {
        var stored = MongoUserMailOutbox.ToEntity(Mail(), Now);
        CaptureUpdate(stored);

        await new MongoUserMailOutbox(_mail, _batches).PostponeAsync(stored.Id.ToString(), "instance-a", "451 try later", Now.AddMinutes(2));

        var update = Render(_update);
        Assert.DoesNotContain("\"Status\"", update);
        Assert.Contains("\"Reason\" : \"451 try later\"", update);
        Assert.Contains("\"NextAttemptAt\"", update);
        Assert.Contains("\"ClaimedUntil\" : 1", update);
    }

    [Fact]
    public void TheQueue_IsIndexedForClaiming_UniqueOnDeduplicationKeyWherePresent_AndExpiresDoneMail()
    {
        var collection = (UserMailCollection)RuntimeHelpers.GetUninitializedObject(typeof(UserMailCollection));
        typeof(UserMailCollection).GetField("_retention", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(collection, TimeSpan.FromDays(30));

        var indices = collection.Indices.ToDictionary(x => x.Options.Name);

        Assert.Contains("Status_NextAttemptAt", indices.Keys);
        Assert.True(indices["DeduplicationKey"].Options.Unique);
        Assert.True(indices["DeduplicationKey"].Options.Sparse);
        Assert.Equal(TimeSpan.FromDays(30), indices["CompletedAt_ttl"].Options.ExpireAfter);
    }

    [Fact]
    public async Task AChoice_IsUpdatedInPlace_WhenOneIsStored()
    {
        var existing = new UserMailPreferenceEntity { UserKey = "user-1", Category = "news", Receives = true, UpdatedAt = Now };
        _preferences.UpdateOneAsync(Arg.Any<FilterDefinition<UserMailPreferenceEntity>>(), Arg.Any<UpdateDefinition<UserMailPreferenceEntity>>(), Arg.Any<OneOption<UserMailPreferenceEntity>>(), Arg.Any<IClientSessionHandle>())
            .Returns(Changed(existing));

        await Store().SetChoiceAsync("user-1", "news", false);

        await _preferences.DidNotReceive().AddAsync(Arg.Any<UserMailPreferenceEntity>());
    }

    [Fact]
    public async Task AFirstChoice_IsInserted()
    {
        UserMailPreferenceEntity added = null;
        _preferences.UpdateOneAsync(Arg.Any<FilterDefinition<UserMailPreferenceEntity>>(), Arg.Any<UpdateDefinition<UserMailPreferenceEntity>>(), Arg.Any<OneOption<UserMailPreferenceEntity>>(), Arg.Any<IClientSessionHandle>())
            .Returns((EntityChangeResult<UserMailPreferenceEntity>)null);
        await _preferences.AddAsync(Arg.Do<UserMailPreferenceEntity>(x => added = x));

        await Store().SetChoiceAsync("user-1", "news", false);

        Assert.Equal(("user-1", "news", false, Now), (added.UserKey, added.Category, added.Receives, added.UpdatedAt));
    }

    [Fact]
    public async Task ClearingAChoice_DeletesThatUsersOneCategory()
    {
        FilterDefinition<UserMailPreferenceEntity> filter = null;
        await _preferences.DeleteManyAsync(Arg.Do<FilterDefinition<UserMailPreferenceEntity>>(x => filter = x), Arg.Any<IClientSessionHandle>());

        await Store().ClearChoiceAsync("user-1", "news");

        var rendered = filter.Render(new RenderArgs<UserMailPreferenceEntity>(BsonSerializer.LookupSerializer<UserMailPreferenceEntity>(), BsonSerializer.SerializerRegistry)).ToString();
        Assert.Contains("\"UserKey\" : \"user-1\"", rendered);
        Assert.Contains("\"Category\" : \"news\"", rendered);
    }

    [Fact]
    public async Task AUsersChoices_AreReadByCategory()
    {
        _preferences.GetAsync(Arg.Any<System.Linq.Expressions.Expression<Func<UserMailPreferenceEntity, bool>>>(), Arg.Any<Options<UserMailPreferenceEntity>>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new UserMailPreferenceEntity { UserKey = "user-1", Category = "news", Receives = false, UpdatedAt = Now },
                new UserMailPreferenceEntity { UserKey = "user-1", Category = "digest", Receives = true, UpdatedAt = Now }
            }.ToAsyncEnumerable());

        var choices = await Store().GetChoicesAsync("user-1");

        Assert.False(choices["news"]);
        Assert.True(choices["digest"]);
    }

    [Fact]
    public void Choices_AreUniquePerUserAndCategory()
    {
        var collection = (UserMailPreferenceCollection)RuntimeHelpers.GetUninitializedObject(typeof(UserMailPreferenceCollection));

        var index = Assert.Single(collection.Indices);
        Assert.True(index.Options.Unique);
        var keys = index.Keys.Render(new RenderArgs<UserMailPreferenceEntity>(BsonSerializer.LookupSerializer<UserMailPreferenceEntity>(), BsonSerializer.SerializerRegistry));
        Assert.Equal(["UserKey", "Category"], keys.Names);
    }

    [Fact]
    public async Task TheRecipient_IsReadFromTheUserRecord()
    {
        var users = Substitute.For<IUserRepository<MongoSupportSenderDirectoryTests.UserEntity>>();
        users.GetByKeyAsync("user-1").Returns(new MongoSupportSenderDirectoryTests.UserEntity { Key = "user-1", EMail = " anna@example.org ", Name = "Anna" });
        var directory = new MongoUserMailRecipientDirectory<MongoSupportSenderDirectoryTests.UserEntity>(users);

        var recipient = await directory.FindAsync("user-1");

        Assert.Equal(("user-1", "anna@example.org", "Anna"), (recipient.Key, recipient.EMail, recipient.Name));
        Assert.Null(await directory.FindAsync("user-2"));
        Assert.Null(await directory.FindAsync(" "));
    }

    [Fact]
    public async Task ManyRecipients_AreReadFromTheUserRecords_InOneRead()
    {
        var users = Substitute.For<IUserRepository<MongoSupportSenderDirectoryTests.UserEntity>>();
        users.GetByKeysAsync(Arg.Any<IReadOnlyCollection<string>>()).Returns(
        [
            new MongoSupportSenderDirectoryTests.UserEntity { Key = "user-1", EMail = "anna@example.org", Name = "Anna" },
            new MongoSupportSenderDirectoryTests.UserEntity { Key = "user-2", Name = " " }
        ]);
        var directory = new MongoUserMailRecipientDirectory<MongoSupportSenderDirectoryTests.UserEntity>(users);

        var found = await directory.FindManyAsync(["user-1", "user-2", "user-1", " "]);

        Assert.Equal(["user-1", "user-2"], found.Select(x => x.Key));
        Assert.Null(found[1].Name);
        await users.Received(1).GetByKeysAsync(Arg.Is<IReadOnlyCollection<string>>(x => x.SequenceEqual(new[] { "user-1", "user-2" })));
        await users.DidNotReceive().GetByKeyAsync(Arg.Any<string>());
    }

    private static string Render(SortDefinition<UserMailEntity> sort)
        => sort.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).ToString();

    private static string Render(ProjectionDefinition<UserMailEntity> projection)
        => projection.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).ToString();

    [Fact]
    public async Task ABatchsMails_AreListedInTheOrderQueued_ByStatus_AfterTheCursor_WithoutTheirFilesBytes()
    {
        FilterDefinition<UserMailEntity> filter = null;
        Options<UserMailEntity> options = null;
        var stored = Enumerable.Range(0, 3).Select(_ => MongoUserMailOutbox.ToBatchItem("batch-1", "u", Batch(), Now)).ToArray();
        _mail.GetManyAsync(Arg.Do<FilterDefinition<UserMailEntity>>(x => filter = x), Arg.Do<Options<UserMailEntity>>(x => options = x), Arg.Any<CancellationToken>())
            .Returns(new Result<UserMailEntity, ObjectId> { Items = stored });
        var after = ObjectId.GenerateNewId().ToString();

        var page = await new MongoUserMailOutbox(_mail, _batches).ListBatchAsync("batch-1", UserMailStatus.Failed, after, 2, TestContext.Current.CancellationToken);

        var rendered = Render(filter);
        Assert.Contains("\"BatchId\" : \"batch-1\"", rendered);
        Assert.Contains("\"Status\" : \"Failed\"", rendered);
        Assert.Contains($"\"_id\" : {{ \"$gt\" : {{ \"$oid\" : \"{after}\" }} }}", rendered);
        Assert.Equal("{ \"_id\" : 1 }", Render(options.Sort));
        Assert.Equal(3, options.Limit);
        Assert.Equal("{ \"Attachments.Content\" : 0 }", Render(options.Projection));
        Assert.Equal(2, page.Items.Length);
        Assert.Equal(stored[1].Id.ToString(), page.NextCursor);
    }

    [Fact]
    public async Task ABatchsLastPage_HasNoCursor()
    {
        _mail.GetManyAsync(Arg.Any<FilterDefinition<UserMailEntity>>(), Arg.Any<Options<UserMailEntity>>(), Arg.Any<CancellationToken>())
            .Returns(new Result<UserMailEntity, ObjectId> { Items = [MongoUserMailOutbox.ToBatchItem("batch-1", "u", Batch(), Now)] });

        var page = await new MongoUserMailOutbox(_mail, _batches).ListBatchAsync("batch-1", null, null, 2, TestContext.Current.CancellationToken);

        Assert.Single(page.Items);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task AUsersMail_IsTheirKeysAndTheirAddressAlones_NewestFirst_BeforeTheCursor()
    {
        FilterDefinition<UserMailEntity> filter = null;
        Options<UserMailEntity> options = null;
        _mail.GetManyAsync(Arg.Do<FilterDefinition<UserMailEntity>>(x => filter = x), Arg.Do<Options<UserMailEntity>>(x => options = x), Arg.Any<CancellationToken>())
            .Returns(new Result<UserMailEntity, ObjectId> { Items = [] });
        var before = ObjectId.GenerateNewId().ToString();

        await new MongoUserMailOutbox(_mail, _batches).ListForRecipientAsync("user-1", "anna@example.org", before, 20, TestContext.Current.CancellationToken);

        var rendered = Render(filter);
        Assert.Contains("\"RecipientUserKey\" : \"user-1\"", rendered);
        Assert.Contains("\"RecipientUserKey\" : null, \"RecipientAddress\" : \"anna@example.org\"", rendered);
        Assert.Contains("$or", rendered);
        Assert.Contains($"\"_id\" : {{ \"$lt\" : {{ \"$oid\" : \"{before}\" }} }}", rendered);
        Assert.Equal("{ \"_id\" : -1 }", Render(options.Sort));
        Assert.Equal("{ \"Attachments.Content\" : 0 }", Render(options.Projection));
    }

    [Fact]
    public void AUserWithNoAddress_IsFoundByKeyAlone_AndNeitherFindsNothing()
    {
        var byKey = Render(MongoUserMailOutbox.ForRecipient("user-1", null));
        Assert.Contains("\"RecipientUserKey\" : \"user-1\"", byKey);
        Assert.DoesNotContain("RecipientAddress", byKey);
        Assert.Contains("\"_id\" : { \"$oid\" : \"000000000000000000000000\" }", Render(MongoUserMailOutbox.ForRecipient(null, null)));
    }

    [Fact]
    public async Task ACursorNotIssuedByTheOutbox_IsRefused()
    {
        var outbox = new MongoUserMailOutbox(_mail, _batches);

        await Assert.ThrowsAsync<ArgumentException>(() => outbox.ListBatchAsync("batch-1", null, "not-a-cursor", 10, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => outbox.ListForRecipientAsync("user-1", null, "not-a-cursor", 10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TheKind_IsStoredByName_AndAMailStoredBeforeKindsWere_IsReadFromWhetherItNamesABatch()
    {
        var notice = MongoUserMailOutbox.ToEntity(Mail() with { Kind = UserMailKind.Notice }, Now);
        Assert.Equal("Notice", notice.ToBsonDocument()[nameof(UserMailEntity.Kind)].AsString);
        Assert.Equal(UserMailKind.Notice, MongoUserMailOutbox.ToItem(notice).Mail.Kind);

        Assert.Equal(UserMailKind.Message, MongoUserMailOutbox.ToItem(MongoUserMailOutbox.ToBatchItem("batch-1", "u", Batch(), Now) with { Kind = null }).Mail.Kind);
        Assert.Equal(UserMailKind.Mail, MongoUserMailOutbox.ToItem(MongoUserMailOutbox.ToEntity(Mail(), Now) with { Kind = null }).Mail.Kind);
        Assert.Equal(UserMailKind.Message, MongoUserMailOutbox.ToItem(MongoUserMailOutbox.ToBatchItem("batch-1", "u", Batch(), Now)).Mail.Kind);
    }

    [Fact]
    public void TheAddressSentTo_IsReadBack_AndIsNotTakenForTheAddressOfAMailToAUser()
    {
        var sent = MongoUserMailOutbox.ToEntity(Mail(), Now) with { RecipientAddress = "anna@example.org" };

        var item = MongoUserMailOutbox.ToItem(sent);

        Assert.Equal("anna@example.org", item.RecipientAddress);
        Assert.Null(item.Mail.RecipientAddress);
    }

    [Fact]
    public void TheHistory_IsIndexedByKeyAndByAddress_NewestFirst()
    {
        var collection = (UserMailCollection)RuntimeHelpers.GetUninitializedObject(typeof(UserMailCollection));
        typeof(UserMailCollection).GetField("_retention", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(collection, TimeSpan.FromDays(30));
        var indices = collection.Indices.ToDictionary(x => x.Options.Name);

        Assert.Equal(["RecipientUserKey", "_id"], indices["RecipientUserKey_Id"].Keys.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).Names);
        Assert.Equal(["RecipientAddress", "_id"], indices["RecipientAddress_Id"].Keys.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).Names);
        Assert.True(indices["MessageId"].Options.Sparse);
    }

    [Fact]
    public async Task AMailSentAtOnce_IsInsertedAlreadyHeld_WithItsFirstAttemptCounted()
    {
        UserMailEntity added = null;
        await _mail.AddAsync(Arg.Do<UserMailEntity>(x => added = x));
        var mail = new UserMail { RecipientAddress = "bea@example.org", RecipientName = "Bea", Category = "account", Subject = "Join", Body = "Join us.", Kind = UserMailKind.Invitation };

        var item = await new MongoUserMailOutbox(_mail, _batches).AddClaimedAsync(mail, Now, "inviter", TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);

        Assert.Equal(("inviter", Now.AddMinutes(5), 1, UserMailStatus.Queued), (added.ClaimedBy, added.ClaimedUntil, added.Attempts, added.Status));
        Assert.Equal(added.Id.ToString(), item.Id);
        Assert.Equal("inviter", item.ClaimedBy);
        Assert.Equal(1, item.Attempts);
    }

    [Fact]
    public async Task AMailSentAtOnce_TakesNoDeduplicationKey()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new MongoUserMailOutbox(_mail, _batches).AddClaimedAsync(Mail("d1"), Now, "inviter", TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken));
        await _mail.DidNotReceive().AddAsync(Arg.Any<UserMailEntity>());
    }

    [Fact]
    public void AMailToAnAddressAlone_KeepsItsAddressAndName_AndHasNoUserKey()
    {
        var mail = new UserMail { RecipientAddress = "bea@example.org", RecipientName = "Bea", Category = "account", Subject = "Join", Body = "Join us.", Kind = UserMailKind.Invitation };

        var entity = MongoUserMailOutbox.ToEntity(mail, Now);
        var document = entity.ToBsonDocument();
        var item = MongoUserMailOutbox.ToItem(entity);

        Assert.Equal(BsonNull.Value, document[nameof(UserMailEntity.RecipientUserKey)]);
        Assert.Equal("bea@example.org", document[nameof(UserMailEntity.RecipientAddress)].AsString);
        Assert.Equal("Invitation", document[nameof(UserMailEntity.Kind)].AsString);
        Assert.Equal("bea@example.org", item.RecipientAddress);
        Assert.Equal(("bea@example.org", "Bea"), (item.Mail.RecipientAddress, item.Mail.RecipientName));
        Assert.Null(item.Mail.RecipientUserKey);
    }

    [Fact]
    public void AMailToAUser_StoresNoQueuedAddressOrName_EvenWhenGivenOne()
    {
        var entity = MongoUserMailOutbox.ToEntity(Mail() with { RecipientAddress = "other@example.org", RecipientName = "Other" }, Now);

        Assert.Null(entity.RecipientAddress);
        Assert.Null(entity.RecipientName);
    }

    private static UserMailBatch Batch() => new() { RecipientUserKeys = ["u"], Category = "news", Subject = "Hello", Body = "Hi" };

    private MongoUserMailPreferenceStore Store()
    {
        var time = Substitute.For<TimeProvider>();
        time.GetUtcNow().Returns(new DateTimeOffset(Now));
        return new MongoUserMailPreferenceStore(_preferences, time);
    }
}
