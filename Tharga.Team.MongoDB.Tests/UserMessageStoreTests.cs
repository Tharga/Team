using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The MongoDB adapters behind staff messages: a batch's content is stored once and its mails name it, its outcome is
/// recorded once every mail is done, the sent log pages newest first, and only active memberships are reached.
/// </summary>
public class UserMessageStoreTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    private readonly IUserMailCollection _mail = Substitute.For<IUserMailCollection>();
    private readonly IUserMailBatchCollection _batches = Substitute.For<IUserMailBatchCollection>();
    private readonly IUserMessageCollection _log = Substitute.For<IUserMessageCollection>();

    private MongoUserMailOutbox Outbox => new(_mail, _batches);

    private static string Render<T>(FilterDefinition<T> filter)
        => filter.Render(new RenderArgs<T>(BsonSerializer.LookupSerializer<T>(), BsonSerializer.SerializerRegistry)).ToString();

    private static string Render<T>(UpdateDefinition<T> update)
        => update.Render(new RenderArgs<T>(BsonSerializer.LookupSerializer<T>(), BsonSerializer.SerializerRegistry)).ToString();

    private static EntityChangeResult<T> Changed<T>(T before)
        => (EntityChangeResult<T>)Activator.CreateInstance(typeof(EntityChangeResult<T>), BindingFlags.NonPublic | BindingFlags.Instance, null, [before, before], null);

    private static UserMailBatch Batch(params string[] recipients) => new()
    {
        RecipientUserKeys = recipients,
        Category = "news",
        Subject = "Maintenance",
        BodyHtml = "<p>Sunday</p>",
        Body = "Sunday",
        Attachments = [new UserMailAttachment { FileName = "plan.pdf", ContentType = "application/pdf", Content = [1, 2, 3], Size = 3 }]
    };

    private void Counts(int queued, int sent, int skipped, int failed)
    {
        _mail.CountAsync(Arg.Any<FilterDefinition<UserMailEntity>>(), Arg.Any<CancellationToken>()).Returns(x =>
        {
            var filter = Render((FilterDefinition<UserMailEntity>)x[0]);
            return filter.Contains("\"Queued\"") ? queued : filter.Contains("\"Sent\"") ? sent : filter.Contains("\"Skipped\"") ? skipped : failed;
        });
        _mail.GetManyAsync(Arg.Any<FilterDefinition<UserMailEntity>>(), Arg.Any<Options<UserMailEntity>>(), Arg.Any<CancellationToken>())
            .Returns(new Result<UserMailEntity, ObjectId> { Items = [] });
    }

    [Fact]
    public async Task ABatch_IsStoredOnce_BeforeOneLightMailPerRecipient()
    {
        UserMailBatchEntity stored = null;
        List<UserMailEntity> items = null;
        await _batches.AddAsync(Arg.Do<UserMailBatchEntity>(x => stored = x));
        await _mail.AddManyAsync(Arg.Do<IEnumerable<UserMailEntity>>(x => items = [.. x]));

        var id = await Outbox.AddBatchAsync(Batch("u-1", "u-2", "u-3"), Now, TestContext.Current.CancellationToken);

        Received.InOrder(() =>
        {
            _batches.AddAsync(Arg.Any<UserMailBatchEntity>());
            _mail.AddManyAsync(Arg.Any<IEnumerable<UserMailEntity>>());
        });
        Assert.Equal(id, stored.Id.ToString());
        Assert.Equal(3, stored.RecipientCount);
        Assert.Equal([1, 2, 3], Assert.Single(stored.Attachments).Content);
        Assert.Equal(["u-1", "u-2", "u-3"], items.Select(x => x.RecipientUserKey));
        Assert.All(items, x =>
        {
            Assert.Equal(id, x.BatchId);
            Assert.Equal(UserMailStatus.Queued, x.Status);
            Assert.Empty(x.Attachments);
            Assert.Null(x.Body);
            Assert.Equal("Maintenance", x.Subject);
        });
    }

    [Fact]
    public void ABatchsContent_SurvivesTheRoundTripToStorage()
    {
        var content = MongoUserMailOutbox.ToBatch(MongoUserMailOutbox.ToBatchEntity(Batch("u-1"), Now));

        Assert.Equivalent(Batch() with { RecipientUserKeys = [] }, content);
    }

    [Fact]
    public void AMailOfABatch_NamesItsBatch()
    {
        var item = MongoUserMailOutbox.ToItem(MongoUserMailOutbox.ToBatchItem("b-1", "u-1", Batch("u-1"), Now));

        Assert.Equal("b-1", item.BatchId);
        Assert.Equal("u-1", item.Mail.RecipientUserKey);
    }

    [Fact]
    public async Task ARecordedOutcome_IsReadAsRecorded_WithoutCounting()
    {
        var entity = MongoUserMailOutbox.ToBatchEntity(Batch("u-1", "u-2"), Now) with
        {
            CompletedAt = Now,
            Outcome = new UserMailBatchOutcomeEntity { Sent = 1, Failed = 1, Failures = [new UserMailFailureEntity { UserKey = "u-2", Reason = "No address." }] }
        };
        _batches.GetOneAsync(Arg.Any<System.Linq.Expressions.Expression<Func<UserMailBatchEntity, bool>>>(), Arg.Any<OneOption<UserMailBatchEntity>>(), Arg.Any<CancellationToken>()).Returns(entity);

        var delivery = await Outbox.GetBatchDeliveryAsync(entity.Id.ToString(), TestContext.Current.CancellationToken);

        Assert.Equal(new UserMailOutcomes { Sent = 1, Failed = 1 }, delivery.Outcomes);
        Assert.Equal("u-2", Assert.Single(delivery.Failures).UserKey);
        Assert.Equal(Now, delivery.CompletedAt);
        await _mail.DidNotReceive().CountAsync(Arg.Any<FilterDefinition<UserMailEntity>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ABatchWithMailStillWaiting_IsCounted_AndNothingIsRecorded()
    {
        var entity = MongoUserMailOutbox.ToBatchEntity(Batch("u-1", "u-2"), Now);
        _batches.GetOneAsync(Arg.Any<System.Linq.Expressions.Expression<Func<UserMailBatchEntity, bool>>>(), Arg.Any<OneOption<UserMailBatchEntity>>(), Arg.Any<CancellationToken>()).Returns(entity);
        Counts(queued: 1, sent: 1, skipped: 0, failed: 0);

        var delivery = await Outbox.GetBatchDeliveryAsync(entity.Id.ToString(), TestContext.Current.CancellationToken);

        Assert.Equal(new UserMailOutcomes { Queued = 1, Sent = 1 }, delivery.Outcomes);
        await _batches.DidNotReceive().UpdateOneAsync(Arg.Any<FilterDefinition<UserMailBatchEntity>>(), Arg.Any<UpdateDefinition<UserMailBatchEntity>>(), Arg.Any<OneOption<UserMailBatchEntity>>(), Arg.Any<IClientSessionHandle>());
    }

    [Fact]
    public async Task ABatchWhoseMailsAreAllDone_HasItsOutcomeRecordedOnce_AndItsFilesBytesRemoved()
    {
        var entity = MongoUserMailOutbox.ToBatchEntity(Batch("u-1", "u-2"), Now);
        _batches.GetOneAsync(Arg.Any<System.Linq.Expressions.Expression<Func<UserMailBatchEntity, bool>>>(), Arg.Any<OneOption<UserMailBatchEntity>>(), Arg.Any<CancellationToken>()).Returns(entity);
        Counts(queued: 0, sent: 1, skipped: 1, failed: 0);
        FilterDefinition<UserMailBatchEntity> filter = null;
        UpdateDefinition<UserMailBatchEntity> update = null;
        await _batches.UpdateOneAsync(Arg.Do<FilterDefinition<UserMailBatchEntity>>(x => filter = x), Arg.Do<UpdateDefinition<UserMailBatchEntity>>(x => update = x), Arg.Any<OneOption<UserMailBatchEntity>>(), Arg.Any<IClientSessionHandle>());

        var delivery = await Outbox.GetBatchDeliveryAsync(entity.Id.ToString(), TestContext.Current.CancellationToken);

        Assert.Equal(new UserMailOutcomes { Sent = 1, Skipped = 1 }, delivery.Outcomes);
        Assert.Contains(entity.Id.ToString(), Render(filter));
        Assert.Contains("\"CompletedAt\" : null", Render(filter));
        var rendered = Render(update);
        Assert.Contains("\"Outcome\"", rendered);
        Assert.Contains("\"Attachments.$[].Content\" : 1", rendered);
    }

    [Fact]
    public void ABatchWhoseMailsWereNotAllInserted_IsNeverDone()
    {
        var delivery = new UserMailBatchDelivery { Id = "b", Outcomes = new UserMailOutcomes { Sent = 2 } };

        Assert.False(MongoUserMailOutbox.IsDone(delivery, 3));
        Assert.True(MongoUserMailOutbox.IsDone(delivery, 2));
        Assert.False(MongoUserMailOutbox.IsDone(delivery with { Outcomes = new UserMailOutcomes { Sent = 2, Queued = 1 } }, 2));
    }

    [Fact]
    public async Task AMailOfABatchDone_WhileAnotherWaits_DoesNotCountTheBatch()
    {
        var stored = MongoUserMailOutbox.ToBatchItem(ObjectId.GenerateNewId().ToString(), "u-1", Batch("u-1"), Now);
        _mail.UpdateOneAsync(Arg.Any<FilterDefinition<UserMailEntity>>(), Arg.Any<UpdateDefinition<UserMailEntity>>(), Arg.Any<OneOption<UserMailEntity>>(), Arg.Any<IClientSessionHandle>()).Returns(Changed(stored));
        _mail.GetManyAsync(Arg.Any<FilterDefinition<UserMailEntity>>(), Arg.Any<Options<UserMailEntity>>(), Arg.Any<CancellationToken>())
            .Returns(new Result<UserMailEntity, ObjectId> { Items = [stored] });

        Assert.True(await Outbox.MarkSentAsync(stored.Id.ToString(), "me", "<m@x>", Now, TestContext.Current.CancellationToken));

        await _batches.DidNotReceive().GetOneAsync(Arg.Any<System.Linq.Expressions.Expression<Func<UserMailBatchEntity, bool>>>(), Arg.Any<OneOption<UserMailBatchEntity>>(), Arg.Any<CancellationToken>());
        await _mail.DidNotReceive().CountAsync(Arg.Any<FilterDefinition<UserMailEntity>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheLastMailOfABatchDone_RecordsTheBatchsOutcome()
    {
        var batch = MongoUserMailOutbox.ToBatchEntity(Batch("u-1"), Now);
        var stored = MongoUserMailOutbox.ToBatchItem(batch.Id.ToString(), "u-1", Batch("u-1"), Now);
        _mail.UpdateOneAsync(Arg.Any<FilterDefinition<UserMailEntity>>(), Arg.Any<UpdateDefinition<UserMailEntity>>(), Arg.Any<OneOption<UserMailEntity>>(), Arg.Any<IClientSessionHandle>()).Returns(Changed(stored));
        _batches.GetOneAsync(Arg.Any<System.Linq.Expressions.Expression<Func<UserMailBatchEntity, bool>>>(), Arg.Any<OneOption<UserMailBatchEntity>>(), Arg.Any<CancellationToken>()).Returns(batch);
        Counts(queued: 0, sent: 1, skipped: 0, failed: 0);

        await Outbox.MarkSentAsync(stored.Id.ToString(), "me", "<m@x>", Now, TestContext.Current.CancellationToken);

        await _batches.Received(1).UpdateOneAsync(Arg.Any<FilterDefinition<UserMailBatchEntity>>(), Arg.Any<UpdateDefinition<UserMailBatchEntity>>(), Arg.Any<OneOption<UserMailBatchEntity>>(), Arg.Any<IClientSessionHandle>());
    }

    [Fact]
    public void ABatchsMails_AreFoundByBatchAndStatus()
    {
        var filter = Render(MongoUserMailOutbox.OfBatch("b-1", UserMailStatus.Failed));

        Assert.Contains("\"BatchId\" : \"b-1\"", filter);
        Assert.Contains("\"Status\" : \"Failed\"", filter);

        var collection = (UserMailCollection)RuntimeHelpers.GetUninitializedObject(typeof(UserMailCollection));
        Assert.Contains(collection.Indices, x => x.Options.Name == "BatchId_Status");
    }

    [Fact]
    public void ADoneBatch_ExpiresAfterTheRetention()
    {
        var collection = (UserMailBatchCollection)RuntimeHelpers.GetUninitializedObject(typeof(UserMailBatchCollection));
        typeof(UserMailBatchCollection).GetField("_retention", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(collection, TimeSpan.FromDays(30));

        Assert.Equal(TimeSpan.FromDays(30), Assert.Single(collection.Indices).Options.ExpireAfter);
    }

    [Fact]
    public void ALogEntry_SurvivesTheRoundTripToStorage_WithItsLevelsByName()
    {
        var entry = new UserMessageLogEntry
        {
            Id = "m-1",
            Recipients = new UserMessageRecipients { TeamKeys = ["t-1"], UserKeys = ["u-1"], Roles = ["Editor"], AccessLevels = [AccessLevel.Owner, AccessLevel.Viewer], EveryMember = true },
            Category = "news",
            Subject = "Maintenance",
            AttachmentCount = 2,
            SentByUserKey = "s-1",
            SentByName = "Sam",
            SentAt = Now,
            RecipientCount = 12
        };

        var entity = MongoUserMessageLog.ToEntity(entry);

        Assert.Equal(["Owner", "Viewer"], entity.Recipients.AccessLevels);
        Assert.Equal(BsonType.String, entity.ToBsonDocument()["Recipients"]["AccessLevels"][0].BsonType);
        Assert.Equivalent(entry, MongoUserMessageLog.ToEntry(entity));
    }

    [Fact]
    public async Task TheLog_PagesNewestFirst_WithACursorForTheNextPage()
    {
        Options<UserMessageEntity> options = null;
        var entities = Enumerable.Range(0, 3).Select(i => MongoUserMessageLog.ToEntity(new UserMessageLogEntry
        {
            Id = $"m-{i}",
            Recipients = new UserMessageRecipients { AllTeams = true, EveryMember = true },
            Category = "news",
            Subject = "S",
            SentAt = Now.AddMinutes(-i),
            RecipientCount = 1
        })).ToArray();
        _log.GetManyAsync(Arg.Any<FilterDefinition<UserMessageEntity>>(), Arg.Do<Options<UserMessageEntity>>(x => options = x), Arg.Any<CancellationToken>())
            .Returns(new Result<UserMessageEntity, ObjectId> { Items = entities });

        var page = await new MongoUserMessageLog(_log).ListAsync(null, 2, TestContext.Current.CancellationToken);

        Assert.Equal(3, options.Limit);
        Assert.Contains("\"SentAt\" : -1", options.Sort.Render(new RenderArgs<UserMessageEntity>(BsonSerializer.LookupSerializer<UserMessageEntity>(), BsonSerializer.SerializerRegistry)).ToString());
        Assert.Equal(["m-0", "m-1"], page.Items.Select(x => x.Id));
        Assert.Equal(MongoUserMessageLog.CursorOf(entities[1]), page.NextCursor);
    }

    [Fact]
    public void TheCursor_ContinuesAfterTheLastMessageShown()
    {
        var entity = MongoUserMessageLog.ToEntity(new UserMessageLogEntry { Id = "m-1", Recipients = new UserMessageRecipients(), Category = "news", Subject = "S", SentAt = Now, RecipientCount = 1 });

        var filter = Render(MongoUserMessageLog.After(MongoUserMessageLog.CursorOf(entity)));

        Assert.Contains("\"SentAt\" : { \"$lt\"", filter);
        Assert.Contains("\"MessageId\" : { \"$gt\" : \"m-1\" }", filter);
        Assert.Throws<ArgumentException>(() => MongoUserMessageLog.After("nonsense"));
    }

    [Fact]
    public void TheLog_IsUniqueOnTheMessage_AndExpiresAfterTheRetention()
    {
        var collection = (UserMessageCollection)RuntimeHelpers.GetUninitializedObject(typeof(UserMessageCollection));
        typeof(UserMessageCollection).GetField("_retention", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(collection, TimeSpan.FromDays(30));

        var indices = collection.Indices.ToDictionary(x => x.Options.Name);

        Assert.True(indices["MessageId"].Options.Unique);
        Assert.Equal(TimeSpan.FromDays(30), indices["SentAt_ttl"].Options.ExpireAfter);
    }

    [Fact]
    public async Task ChoicesForACategory_AreReadInSlicesOfUsers()
    {
        var preferences = Substitute.For<IUserMailPreferenceCollection>();
        var filters = new List<FilterDefinition<UserMailPreferenceEntity>>();
        preferences.GetAsync(Arg.Do<FilterDefinition<UserMailPreferenceEntity>>(filters.Add), Arg.Any<Options<UserMailPreferenceEntity>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new UserMailPreferenceEntity { UserKey = "u-1", Category = "news", Receives = false, UpdatedAt = Now } }.ToAsyncEnumerable());
        var store = new MongoUserMailPreferenceStore(preferences, TimeProvider.System);
        string[] keys = [.. Enumerable.Range(0, MongoUserMailPreferenceStore.KeysPerQuery + 1).Select(x => $"u-{x}")];

        var choices = await store.GetChoicesForCategoryAsync("news", keys, TestContext.Current.CancellationToken);

        Assert.Equal(2, filters.Count);
        Assert.Contains("\"Category\" : \"news\"", Render(filters[0]));
        Assert.Contains("\"UserKey\" : { \"$in\"", Render(filters[0]));
        Assert.False(choices["u-1"]);
    }

    [Fact]
    public void WithBothRepositories_TheMessagePortsAreRegistered()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamRepository(o =>
        {
            o.RegisterUserRepository<MongoSupportSenderDirectoryTests.UserEntity>();
            o.RegisterTeamRepository<MongoSupportSenderDirectoryTests.TeamEntity, MongoSupportSenderDirectoryTests.Member>();
        });

        Assert.Equal(typeof(MongoUserMessageMemberDirectory<MongoSupportSenderDirectoryTests.UserEntity, MongoSupportSenderDirectoryTests.TeamEntity, MongoSupportSenderDirectoryTests.Member>),
            services.Single(x => x.ServiceType == typeof(IUserMessageMemberDirectory)).ImplementationType);
        Assert.Equal(typeof(MongoUserMessageLog), services.Single(x => x.ServiceType == typeof(IUserMessageLog)).ImplementationType);
        Assert.Contains(services, x => x.ServiceType == typeof(IUserMailBatchCollection));
    }

    [Fact]
    public void WithoutAUserRepository_NoMemberDirectoryIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamRepository(o => o.RegisterTeamRepository<MongoSupportSenderDirectoryTests.TeamEntity, MongoSupportSenderDirectoryTests.Member>());

        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IUserMessageMemberDirectory));
    }
}
