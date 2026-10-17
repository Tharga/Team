using System.Reflection;
using System.Runtime.CompilerServices;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.MongoDB;
using TeamEntity = Tharga.Team.MongoDB.Tests.MongoSupportSenderDirectoryTests.TeamEntity;
using UserEntity = Tharga.Team.MongoDB.Tests.MongoSupportSenderDirectoryTests.UserEntity;
using Member = Tharga.Team.MongoDB.Tests.MongoSupportSenderDirectoryTests.Member;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The MongoDB adapters behind automatic notices: claims and outcomes are each one conditional find-and-modify, a user's
/// notice is stored once per occurrence, a stream's due time only moves earlier while notices arrive, a release never
/// leaves a notice without a due time, and choices and team defaults are stored only while they say something.
/// </summary>
public class UserNoticeStoreTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    private readonly IUserNoticeCollection _notices = Substitute.For<IUserNoticeCollection>();
    private readonly IUserNoticeItemCollection _items = Substitute.For<IUserNoticeItemCollection>();
    private readonly IUserNoticeStreamCollection _streams = Substitute.For<IUserNoticeStreamCollection>();
    private readonly IUserNoticePreferenceCollection _choices = Substitute.For<IUserNoticePreferenceCollection>();
    private readonly IUserNoticeTeamDefaultCollection _teamDefaults = Substitute.For<IUserNoticeTeamDefaultCollection>();

    private MongoUserNoticeOutbox Outbox => new(_notices, _items, _streams);

    private MongoUserNoticePreferenceStore Preferences
    {
        get
        {
            var time = Substitute.For<TimeProvider>();
            time.GetUtcNow().Returns(new DateTimeOffset(Now));
            return new MongoUserNoticePreferenceStore(_choices, _teamDefaults, time);
        }
    }

    private static string Render<T>(FilterDefinition<T> filter)
        => filter.Render(new RenderArgs<T>(BsonSerializer.LookupSerializer<T>(), BsonSerializer.SerializerRegistry)).ToString();

    private static string Render<T>(UpdateDefinition<T> update)
        => update.Render(new RenderArgs<T>(BsonSerializer.LookupSerializer<T>(), BsonSerializer.SerializerRegistry)).ToString();

    private static EntityChangeResult<T> Changed<T>(T before)
        => (EntityChangeResult<T>)Activator.CreateInstance(typeof(EntityChangeResult<T>), BindingFlags.NonPublic | BindingFlags.Instance, null, [before, before], null);

    private static UserNoticeOccurrence Occurrence() => new()
    {
        NoticeKey = "case.assigned",
        TeamKey = "acme",
        SubjectId = "42",
        SubjectText = "Case 42",
        Discriminator = "first",
        Values = new Dictionary<string, string> { ["actor"] = "Bo" },
        LinkPath = "/cases/42",
        Recipients = ["u-1"]
    };

    private static UserNoticeItem Item() => new()
    {
        NoticeId = "n-1",
        OccurrenceKey = "case.assigned:2:42:first",
        UserKey = "u-1",
        TeamKey = "acme",
        NoticeKey = "case.assigned",
        Category = "work",
        Stream = "notice:case.assigned",
        Delivery = UserNoticeDelivery.AtOnce,
        SubjectId = "42",
        SubjectText = "Case 42",
        Values = new Dictionary<string, string> { ["actor"] = "Bo" },
        LinkPath = "/cases/42",
        RaisedAt = Now
    };

    private static UserNoticeStreamEntity Stream(long version = 3) => new()
    {
        Id = ObjectId.GenerateNewId(),
        UserKey = "u-1",
        Stream = "notice:case.assigned",
        DueAt = Now,
        Version = version,
        ClaimedBy = "instance-a",
        ClaimedUntil = Now.AddMinutes(5)
    };

    private static MongoWriteException DuplicateKey()
    {
        var ex = (MongoWriteException)RuntimeHelpers.GetUninitializedObject(typeof(MongoWriteException));
        var writeError = (WriteError)RuntimeHelpers.GetUninitializedObject(typeof(WriteError));

        typeof(WriteError).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(f => f.FieldType == typeof(ServerErrorCategory)).SetValue(writeError, ServerErrorCategory.DuplicateKey);
        typeof(MongoWriteException).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(f => f.FieldType == typeof(WriteError)).SetValue(ex, writeError);

        return ex;
    }

    [Fact]
    public void ARaisedOccurrence_SurvivesTheRoundTripToStorage()
    {
        var record = MongoUserNoticeOutbox.ToRecord(MongoUserNoticeOutbox.ToEntity(Occurrence(), Now));

        Assert.Equivalent(Occurrence(), record.Occurrence);
        Assert.Equal(UserNoticeStatus.Pending, record.Status);
        Assert.Equal(Now, record.NextAttemptAt);
    }

    [Fact]
    public void AUsersNotice_SurvivesTheRoundTripToStorage_AsWaiting()
    {
        var item = MongoUserNoticeOutbox.ToItem(MongoUserNoticeOutbox.ToItemEntity(Item() with { Status = UserNoticeItemStatus.Mailed }));

        Assert.Equivalent(Item() with { Id = item.Id, Status = UserNoticeItemStatus.Waiting }, item);
        Assert.NotNull(item.Id);
    }

    [Fact]
    public void AnOccurrenceIsClaimable_WhenWaitingDue_AndNobodyHoldsIt()
    {
        var filter = Render(MongoUserNoticeOutbox.Claimable(Now));

        Assert.Contains("\"Status\" : \"Pending\"", filter);
        Assert.Contains("\"NextAttemptAt\" : { \"$lte\"", filter);
        Assert.Contains("\"ClaimedUntil\" : null", filter);
        Assert.Contains("\"ClaimedUntil\" : { \"$lte\"", filter);
    }

    [Fact]
    public async Task ClaimingAnOccurrence_IsOneFindAndModify_CountingTheAttempt()
    {
        UpdateDefinition<UserNoticeEntity> update = null;
        OneOption<UserNoticeEntity> options = null;
        var stored = MongoUserNoticeOutbox.ToEntity(Occurrence(), Now);
        _notices.UpdateOneAsync(Arg.Any<FilterDefinition<UserNoticeEntity>>(), Arg.Do<UpdateDefinition<UserNoticeEntity>>(x => update = x), Arg.Do<OneOption<UserNoticeEntity>>(x => options = x), Arg.Any<IClientSessionHandle>())
            .Returns(Changed(stored));

        var claimed = await Outbox.ClaimNextAsync("instance-a", Now, TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);

        Assert.Equal(EMode.FirstOrDefault, options.Mode);
        Assert.Contains("\"$inc\" : { \"Attempts\" : 1 }", Render(update));
        Assert.Contains("\"ClaimedBy\" : \"instance-a\"", Render(update));
        Assert.Equal(1, claimed.Attempts);
        Assert.Equal(stored.Id.ToString(), claimed.Id);
    }

    [Fact]
    public async Task AnOutcome_IsWrittenOnlyWhileTheClaimantHoldsTheOccurrence()
    {
        FilterDefinition<UserNoticeEntity> filter = null;
        UpdateDefinition<UserNoticeEntity> update = null;
        _notices.UpdateOneAsync(Arg.Do<FilterDefinition<UserNoticeEntity>>(x => filter = x), Arg.Do<UpdateDefinition<UserNoticeEntity>>(x => update = x), Arg.Any<OneOption<UserNoticeEntity>>(), Arg.Any<IClientSessionHandle>())
            .Returns((EntityChangeResult<UserNoticeEntity>)null);

        var recorded = await Outbox.MarkDistributedAsync(ObjectId.GenerateNewId().ToString(), "instance-a", 3, Now, TestContext.Current.CancellationToken);

        Assert.False(recorded);
        Assert.Contains("\"ClaimedBy\" : \"instance-a\"", Render(filter));
        Assert.Contains("\"Status\" : \"Distributed\"", Render(update));
        Assert.Contains("\"RecipientCount\" : 3", Render(update));
        Assert.Contains("\"$unset\"", Render(update));
    }

    [Fact]
    public async Task ANewNotice_IsStored_ThenItsStreamMadeDue_CreatingTheStreamWhenThereIsNone()
    {
        UserNoticeStreamEntity created = null;
        _streams.UpdateOneAsync(Arg.Any<FilterDefinition<UserNoticeStreamEntity>>(), Arg.Any<UpdateDefinition<UserNoticeStreamEntity>>(), Arg.Any<OneOption<UserNoticeStreamEntity>>(), Arg.Any<IClientSessionHandle>())
            .Returns((EntityChangeResult<UserNoticeStreamEntity>)null);
        _streams.TryAddAsync(Arg.Do<UserNoticeStreamEntity>(x => created = x), Arg.Any<IClientSessionHandle>()).Returns(true);

        var stored = await Outbox.AddItemAsync(Item(), Now.AddMinutes(2), TestContext.Current.CancellationToken);

        Assert.True(stored);
        Received.InOrder(() =>
        {
            _items.AddAsync(Arg.Any<UserNoticeItemEntity>(), Arg.Any<IClientSessionHandle>());
            _streams.UpdateOneAsync(Arg.Any<FilterDefinition<UserNoticeStreamEntity>>(), Arg.Any<UpdateDefinition<UserNoticeStreamEntity>>(), Arg.Any<OneOption<UserNoticeStreamEntity>>(), Arg.Any<IClientSessionHandle>());
            _streams.TryAddAsync(Arg.Any<UserNoticeStreamEntity>(), Arg.Any<IClientSessionHandle>());
        });
        Assert.Equal(("u-1", "notice:case.assigned", Now.AddMinutes(2), 1L), (created.UserKey, created.Stream, created.DueAt, created.Version));
    }

    [Fact]
    public void MakingAStreamDue_OnlyMovesItEarlier_AndRaisesItsVersion()
    {
        var update = Render(MongoUserNoticeOutbox.MakeDue(Now));

        Assert.Contains("\"$min\" : { \"DueAt\"", update);
        Assert.Contains("\"$inc\" : { \"Version\" : 1 }", update);
    }

    [Fact]
    public async Task TheSameOccurrenceForTheSameUser_IsNotStoredAgain_ButAWaitingOneKeepsItsStreamDue()
    {
        _items.AddAsync(Arg.Any<UserNoticeItemEntity>(), Arg.Any<IClientSessionHandle>()).Returns(Task.FromException(DuplicateKey()));
        _items.GetOneAsync(Arg.Any<FilterDefinition<UserNoticeItemEntity>>(), Arg.Any<OneOption<UserNoticeItemEntity>>(), Arg.Any<CancellationToken>())
            .Returns(MongoUserNoticeOutbox.ToItemEntity(Item()));
        _streams.UpdateOneAsync(Arg.Any<FilterDefinition<UserNoticeStreamEntity>>(), Arg.Any<UpdateDefinition<UserNoticeStreamEntity>>(), Arg.Any<OneOption<UserNoticeStreamEntity>>(), Arg.Any<IClientSessionHandle>())
            .Returns(Changed(Stream()));

        Assert.False(await Outbox.AddItemAsync(Item(), Now, TestContext.Current.CancellationToken));
        await _streams.Received(1).UpdateOneAsync(Arg.Any<FilterDefinition<UserNoticeStreamEntity>>(), Arg.Any<UpdateDefinition<UserNoticeStreamEntity>>(), Arg.Any<OneOption<UserNoticeStreamEntity>>(), Arg.Any<IClientSessionHandle>());
    }

    [Fact]
    public async Task TheSameOccurrenceAlreadyMailed_LeavesTheStreamAlone()
    {
        _items.AddAsync(Arg.Any<UserNoticeItemEntity>(), Arg.Any<IClientSessionHandle>()).Returns(Task.FromException(DuplicateKey()));
        _items.GetOneAsync(Arg.Any<FilterDefinition<UserNoticeItemEntity>>(), Arg.Any<OneOption<UserNoticeItemEntity>>(), Arg.Any<CancellationToken>())
            .Returns(MongoUserNoticeOutbox.ToItemEntity(Item()) with { Status = UserNoticeItemStatus.Mailed });

        Assert.False(await Outbox.AddItemAsync(Item(), Now, TestContext.Current.CancellationToken));
        await _streams.DidNotReceiveWithAnyArgs().UpdateOneAsync(default(FilterDefinition<UserNoticeStreamEntity>), default, default, default);
    }

    [Fact]
    public void AStreamIsDue_WhenItsTimeHasCome_AndNobodyHoldsIt()
    {
        var filter = Render(MongoUserNoticeOutbox.Due(Now));

        Assert.Contains("\"DueAt\" : { \"$lte\"", filter);
        Assert.Contains("\"ClaimedUntil\" : null", filter);
    }

    [Fact]
    public async Task ClaimingAStream_HandsOverAMailAnotherInstanceBegan()
    {
        var stream = Stream() with { MailKey = "notice:begun", MailItemIds = ["i-1"] };
        _streams.UpdateOneAsync(Arg.Any<FilterDefinition<UserNoticeStreamEntity>>(), Arg.Any<UpdateDefinition<UserNoticeStreamEntity>>(), Arg.Any<OneOption<UserNoticeStreamEntity>>(), Arg.Any<IClientSessionHandle>())
            .Returns(Changed(stream));

        var claim = await Outbox.ClaimDueStreamAsync("instance-b", Now, TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);

        Assert.Equal(("u-1", "notice:case.assigned", "notice:begun"), (claim.UserKey, claim.Stream, claim.MailKey));
        Assert.Equal(["i-1"], claim.MailItemIds);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(0, true)]
    public async Task ReleasingAStream_SetsTheNextDueTime_OnlyWhileSomethingWaits(int waiting, bool idle)
    {
        UpdateDefinition<UserNoticeStreamEntity> update = null;
        _streams.GetOneAsync(Arg.Any<FilterDefinition<UserNoticeStreamEntity>>(), Arg.Any<OneOption<UserNoticeStreamEntity>>(), Arg.Any<CancellationToken>()).Returns(Stream());
        _items.CountAsync(Arg.Any<FilterDefinition<UserNoticeItemEntity>>(), Arg.Any<CancellationToken>()).Returns(waiting);
        _streams.UpdateOneAsync(Arg.Any<FilterDefinition<UserNoticeStreamEntity>>(), Arg.Do<UpdateDefinition<UserNoticeStreamEntity>>(x => update = x), Arg.Any<OneOption<UserNoticeStreamEntity>>(), Arg.Any<IClientSessionHandle>())
            .Returns(Changed(Stream()));

        Assert.True(await Outbox.ReleaseStreamAsync("u-1", "notice:case.assigned", "instance-a", Now.AddMinutes(2), TestContext.Current.CancellationToken));

        var rendered = Render(update);
        Assert.Contains(idle ? "9999-12-31" : "2026-10-17T12:02:00", rendered);
        Assert.Contains("\"MailKey\" : 1", rendered);
        Assert.Contains("\"ClaimedBy\" : 1", rendered);
    }

    [Fact]
    public async Task ANoticeAddedWhileReleasing_MakesTheReleaseCountAgain()
    {
        var filters = new List<string>();
        _streams.GetOneAsync(Arg.Any<FilterDefinition<UserNoticeStreamEntity>>(), Arg.Any<OneOption<UserNoticeStreamEntity>>(), Arg.Any<CancellationToken>()).Returns(Stream(3), Stream(4));
        _items.CountAsync(Arg.Any<FilterDefinition<UserNoticeItemEntity>>(), Arg.Any<CancellationToken>()).Returns(0, 1);
        _streams.UpdateOneAsync(Arg.Do<FilterDefinition<UserNoticeStreamEntity>>(x => filters.Add(Render(x))), Arg.Any<UpdateDefinition<UserNoticeStreamEntity>>(), Arg.Any<OneOption<UserNoticeStreamEntity>>(), Arg.Any<IClientSessionHandle>())
            .Returns((EntityChangeResult<UserNoticeStreamEntity>)null, Changed(Stream(4)));

        Assert.True(await Outbox.ReleaseStreamAsync("u-1", "notice:case.assigned", "instance-a", Now.AddMinutes(2), TestContext.Current.CancellationToken));

        Assert.Matches(@"""Version"" : \D*3\D", filters[0]);
        Assert.Matches(@"""Version"" : \D*4\D", filters[1]);
    }

    [Fact]
    public async Task AStreamTheClaimantNoLongerHolds_IsNotReleasedByIt()
    {
        _streams.GetOneAsync(Arg.Any<FilterDefinition<UserNoticeStreamEntity>>(), Arg.Any<OneOption<UserNoticeStreamEntity>>(), Arg.Any<CancellationToken>()).Returns((UserNoticeStreamEntity)null);

        Assert.False(await Outbox.ReleaseStreamAsync("u-1", "notice:case.assigned", "instance-a", Now, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompletingNotices_TouchesOnlyThoseStillWaiting_AndStoresTheStatusByName()
    {
        FilterDefinition<UserNoticeItemEntity> filter = null;
        UpdateDefinition<UserNoticeItemEntity> update = null;
        await _items.UpdateManyAsync(Arg.Do<FilterDefinition<UserNoticeItemEntity>>(x => filter = x), Arg.Do<UpdateDefinition<UserNoticeItemEntity>>(x => update = x), Arg.Any<IClientSessionHandle>());

        await Outbox.CompleteItemsAsync([ObjectId.GenerateNewId().ToString(), "not-an-id"], UserNoticeItemStatus.Mailed, "mail-1", null, Now, TestContext.Current.CancellationToken);

        Assert.Contains("\"Status\" : \"Waiting\"", Render(filter));
        Assert.Contains("\"Status\" : \"Mailed\"", Render(update));
        Assert.Contains("\"MailId\" : \"mail-1\"", Render(update));
    }

    [Fact]
    public void AStreamsWaitingNotices_AreFoundByUserStreamAndStatus()
    {
        var filter = Render(MongoUserNoticeOutbox.Waiting("u-1", "digest:work"));

        Assert.Contains("\"UserKey\" : \"u-1\"", filter);
        Assert.Contains("\"Stream\" : \"digest:work\"", filter);
        Assert.Contains("\"Status\" : \"Waiting\"", filter);
    }

    [Fact]
    public async Task AnEmptyChoice_RemovesTheDocument()
    {
        FilterDefinition<UserNoticePreferenceEntity> filter = null;
        await _choices.DeleteManyAsync(Arg.Do<FilterDefinition<UserNoticePreferenceEntity>>(x => filter = x), Arg.Any<IClientSessionHandle>());

        await Preferences.SetChoiceAsync("u-1", "case.opened", new UserNoticeSetting(), TestContext.Current.CancellationToken);

        Assert.Contains("\"UserKey\" : \"u-1\"", Render(filter));
        Assert.Contains("\"NoticeKey\" : \"case.opened\"", Render(filter));
        await _choices.DidNotReceiveWithAnyArgs().AddAsync(default);
    }

    [Fact]
    public async Task AChoiceNobodyMadeBefore_IsInserted_WithTheDeliveryByName()
    {
        UserNoticePreferenceEntity added = null;
        _choices.UpdateOneAsync(Arg.Any<FilterDefinition<UserNoticePreferenceEntity>>(), Arg.Any<UpdateDefinition<UserNoticePreferenceEntity>>(), Arg.Any<OneOption<UserNoticePreferenceEntity>>(), Arg.Any<IClientSessionHandle>())
            .Returns((EntityChangeResult<UserNoticePreferenceEntity>)null);
        await _choices.AddAsync(Arg.Do<UserNoticePreferenceEntity>(x => added = x), Arg.Any<IClientSessionHandle>());

        await Preferences.SetChoiceAsync("u-1", "case.opened", new UserNoticeSetting { Delivery = UserNoticeDelivery.AtOnce }, TestContext.Current.CancellationToken);

        Assert.Equal(("u-1", "case.opened", null, UserNoticeDelivery.AtOnce, Now), (added.UserKey, added.NoticeKey, added.Enabled, added.Delivery, added.UpdatedAt));
        var bson = added.ToBsonDocument();
        Assert.Equal(BsonType.String, bson["Delivery"].BsonType);
        Assert.False(bson.Contains("Enabled"));
    }

    [Fact]
    public async Task ChoicesForANotice_AreAskedForTheUsersNamed()
    {
        FilterDefinition<UserNoticePreferenceEntity> filter = null;
        _choices.GetAsync(Arg.Do<FilterDefinition<UserNoticePreferenceEntity>>(x => filter = x), Arg.Any<Options<UserNoticePreferenceEntity>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new UserNoticePreferenceEntity { UserKey = "u-2", NoticeKey = "case.opened", Enabled = false, UpdatedAt = Now } }.ToAsyncEnumerable());

        var found = await Preferences.GetChoicesForNoticeAsync("case.opened", ["u-1", "u-2", "u-2"], TestContext.Current.CancellationToken);

        Assert.Contains("\"$in\" : [\"u-1\", \"u-2\"]", Render(filter));
        Assert.False(Assert.Single(found).Value.Enabled);
    }

    [Fact]
    public async Task ATeamsDefaults_AreRemovedByTeam()
    {
        FilterDefinition<UserNoticeTeamDefaultEntity> filter = null;
        _teamDefaults.DeleteManyAsync(Arg.Do<FilterDefinition<UserNoticeTeamDefaultEntity>>(x => filter = x), Arg.Any<IClientSessionHandle>()).Returns(2);

        Assert.Equal(2, await Preferences.RemoveTeamDefaultsAsync("acme", TestContext.Current.CancellationToken));
        Assert.Contains("\"TeamKey\" : \"acme\"", Render(filter));
    }

    [Fact]
    public async Task AMembersScopeOverrides_AreRead_AndAUsersMembershipsAreTheirsAlone()
    {
        var users = Substitute.For<IUserRepository<UserEntity>>();
        var teams = Substitute.For<ITeamRepository<TeamEntity, Member>>();
        users.GetByKeysAsync(Arg.Any<IReadOnlyCollection<string>>()).Returns(x => ((IReadOnlyCollection<string>)x[0]).Select(k => new UserEntity { Key = k, Identity = k }).ToArray());
        teams.GetTeamsByUserAsync("u-1").Returns(new[]
        {
            new TeamEntity { Key = "acme", Name = "Acme", Members = [new Member { Key = "u-1", State = MembershipState.Member, AccessLevel = AccessLevel.Viewer, ScopeOverrides = ["case:read"] }, new Member { Key = "u-2", State = MembershipState.Member }] },
            new TeamEntity { Key = "gone", Name = "Gone", DeletedAt = Now, Members = [new Member { Key = "u-1", State = MembershipState.Member }] }
        }.ToAsyncEnumerable());

        var memberships = await new MongoUserMessageMemberDirectory<UserEntity, TeamEntity, Member>(users, teams).GetActiveMembershipsOfUserAsync("u-1", TestContext.Current.CancellationToken);

        var membership = Assert.Single(memberships);
        Assert.Equal(("acme", "u-1"), (membership.TeamKey, membership.UserKey));
        Assert.Equal(["case:read"], membership.ScopeOverrides);
        await users.Received(1).GetByKeysAsync(Arg.Is<IReadOnlyCollection<string>>(x => x.SequenceEqual(new[] { "u-1" })));
    }
}
