using System.Reflection;
using System.Runtime.CompilerServices;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The MongoDB side of mail sent outside the queue, such as a support answer or a receipt: stored already done so nothing
/// claims it, a second attempt at the same case entry written over the first, its kind stored by name, and found in the log
/// by kind but never in a user's own history.
/// </summary>
public class UserMailRecordStoreTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly UserMailHeaders Headers = new("support@example.net", "Example Support", "case-1@example.net");

    private readonly IUserMailCollection _mail = Substitute.For<IUserMailCollection>();
    private readonly IUserMailBatchCollection _batches = Substitute.For<IUserMailBatchCollection>();

    private static string Render(FilterDefinition<UserMailEntity> filter)
        => filter.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).ToString();

    private static string Render(UpdateDefinition<UserMailEntity> update)
        => update.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).ToString();

    private static EntityChangeResult<T> Changed<T>(T before)
        => (EntityChangeResult<T>)Activator.CreateInstance(typeof(EntityChangeResult<T>), BindingFlags.NonPublic | BindingFlags.Instance, null, [before, before], null);

    private MongoUserMailOutbox Outbox() => new(_mail, _batches);

    private static UserMailRecord Answer() => new()
    {
        Kind = UserMailKind.SupportAnswer,
        RecipientAddress = " Kund@Customer.Example ",
        Subject = "Re: Export is empty",
        TeamKey = "acme",
        SentByUserKey = "staff-1",
        SentByName = "Sam Staff",
        Headers = Headers,
        Status = UserMailStatus.Sent,
        MessageId = "<answer-2@example.net>",
        At = Now,
        CaseId = "case-1",
        CaseMessageSequence = 2
    };

    private void NothingRecordedYet()
        => _mail.GetManyAsync(Arg.Any<FilterDefinition<UserMailEntity>>(), Arg.Any<Options<UserMailEntity>>(), Arg.Any<CancellationToken>())
            .Returns(new Result<UserMailEntity, ObjectId> { Items = [] });

    [Fact]
    public async Task ASentAnswer_IsStoredDone_WithItsHeadersSenderAndCase_AndNeverClaimable()
    {
        NothingRecordedYet();
        UserMailEntity added = null;
        await _mail.AddAsync(Arg.Do<UserMailEntity>(x => added = x));

        var id = await Outbox().RecordAsync(Answer(), TestContext.Current.CancellationToken);

        Assert.Equal(added.Id.ToString(), id);
        Assert.Equal((UserMailStatus.Sent, 1, (DateTime?)Now, Now), (added.Status, added.Attempts, added.CompletedAt, added.QueuedAt));
        Assert.Equal(UserMailKind.SupportAnswer, added.Kind);
        Assert.Equal("kund@customer.example", added.RecipientAddress);
        Assert.Null(added.RecipientUserKey);
        Assert.Null(added.Category);
        Assert.Null(added.Body);
        Assert.Empty(added.Attachments);
        Assert.Equal(("acme", "staff-1", "Sam Staff"), (added.TeamKey, added.SentByUserKey, added.SentByName));
        Assert.Equal(("support@example.net", "Example Support", "case-1@example.net"), (added.FromAddress, added.FromName, added.EffectiveReplyTo));
        Assert.Equal("<answer-2@example.net>", added.MessageId);
        Assert.Equal(("case-1", (int?)2), (added.CaseId, added.CaseMessageSequence));
        Assert.Contains("\"Status\" : \"Queued\"", Render(MongoUserMailOutbox.Claimable(Now.AddYears(1))));
    }

    [Fact]
    public async Task AFailedAnswer_IsStoredGivenUp_WithTheReason()
    {
        NothingRecordedYet();
        UserMailEntity added = null;
        await _mail.AddAsync(Arg.Do<UserMailEntity>(x => added = x));

        await Outbox().RecordAsync(Answer() with { Status = UserMailStatus.Failed, MessageId = null, Reason = "550 5.7.1 Relaying denied" }, TestContext.Current.CancellationToken);

        Assert.Equal((UserMailStatus.Failed, "550 5.7.1 Relaying denied", (string)null), (added.Status, added.Reason, added.MessageId));
    }

    [Theory]
    [InlineData(UserMailStatus.Queued)]
    [InlineData(UserMailStatus.Skipped)]
    [InlineData(UserMailStatus.Bounced)]
    public async Task OnlyASentOrFailedMail_CanBeRecorded(UserMailStatus status)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Outbox().RecordAsync(Answer() with { Status = status }, TestContext.Current.CancellationToken));

        await _mail.DidNotReceiveWithAnyArgs().AddAsync(default);
    }

    [Fact]
    public async Task ASecondAttemptAtTheSameCaseEntry_UpdatesTheRowRecordedFirst()
    {
        var existing = MongoUserMailOutbox.ToRecordedEntity(Answer() with { Status = UserMailStatus.Failed, MessageId = null, Reason = "refused" });
        FilterDefinition<UserMailEntity> lookup = null;
        UpdateDefinition<UserMailEntity> update = null;
        _mail.GetManyAsync(Arg.Do<FilterDefinition<UserMailEntity>>(x => lookup = x), Arg.Any<Options<UserMailEntity>>(), Arg.Any<CancellationToken>())
            .Returns(new Result<UserMailEntity, ObjectId> { Items = [existing] });
        _mail.UpdateOneAsync(Arg.Any<FilterDefinition<UserMailEntity>>(), Arg.Do<UpdateDefinition<UserMailEntity>>(x => update = x), Arg.Any<OneOption<UserMailEntity>>(), Arg.Any<IClientSessionHandle>())
            .Returns(Changed(existing));

        var id = await Outbox().RecordAsync(Answer() with { At = Now.AddHours(1), Headers = Headers with { FromAddress = "anna@example.net" } }, TestContext.Current.CancellationToken);

        Assert.Equal(existing.Id.ToString(), id);
        await _mail.DidNotReceiveWithAnyArgs().AddAsync(default);
        var found = Render(lookup);
        Assert.Contains("\"CaseId\" : \"case-1\"", found);
        Assert.Contains("\"CaseMessageSequence\" : 2", found);
        Assert.Contains("\"Kind\" : \"SupportAnswer\"", found);
        var written = Render(update);
        Assert.Contains("\"Status\" : \"Sent\"", written);
        Assert.Contains("\"MessageId\" : \"<answer-2@example.net>\"", written);
        Assert.Contains("\"FromAddress\" : \"anna@example.net\"", written);
        Assert.Contains("\"Attempts\" : 1", written);
        Assert.Contains("\"Reason\" : 1", written);
        Assert.Contains("\"BouncedAt\" : 1", written);
        Assert.DoesNotContain("SentBy", written);
    }

    [Fact]
    public async Task AFailedSecondAttempt_ForgetsTheMessageIdOfTheFirst()
    {
        var existing = MongoUserMailOutbox.ToRecordedEntity(Answer());
        UpdateDefinition<UserMailEntity> update = null;
        _mail.GetManyAsync(Arg.Any<FilterDefinition<UserMailEntity>>(), Arg.Any<Options<UserMailEntity>>(), Arg.Any<CancellationToken>())
            .Returns(new Result<UserMailEntity, ObjectId> { Items = [existing] });
        _mail.UpdateOneAsync(Arg.Any<FilterDefinition<UserMailEntity>>(), Arg.Do<UpdateDefinition<UserMailEntity>>(x => update = x), Arg.Any<OneOption<UserMailEntity>>(), Arg.Any<IClientSessionHandle>())
            .Returns(Changed(existing));

        await Outbox().RecordAsync(Answer() with { Status = UserMailStatus.Failed, MessageId = null, Reason = "timeout" }, TestContext.Current.CancellationToken);

        var written = Render(update);
        Assert.Contains("\"Status\" : \"Failed\"", written);
        Assert.Contains("\"MessageId\" : 1", written);
        Assert.Contains("\"Reason\" : \"timeout\"", written);
    }

    [Fact]
    public async Task AReceipt_IsKeyedByItsCaseAlone()
    {
        FilterDefinition<UserMailEntity> lookup = null;
        _mail.GetManyAsync(Arg.Do<FilterDefinition<UserMailEntity>>(x => lookup = x), Arg.Any<Options<UserMailEntity>>(), Arg.Any<CancellationToken>())
            .Returns(new Result<UserMailEntity, ObjectId> { Items = [] });

        await Outbox().RecordAsync(Answer() with { Kind = UserMailKind.Receipt, CaseMessageSequence = null, SentByUserKey = null, SentByName = null, TeamKey = null }, TestContext.Current.CancellationToken);

        var found = Render(lookup);
        Assert.Contains("\"Kind\" : \"Receipt\"", found);
        Assert.Contains("\"CaseMessageSequence\" : null", found);
    }

    [Fact]
    public async Task AMailNamingNoCase_IsAlwaysAddedAnew()
    {
        await Outbox().RecordAsync(Answer() with { CaseId = null, CaseMessageSequence = null }, TestContext.Current.CancellationToken);

        await _mail.DidNotReceiveWithAnyArgs().GetManyAsync(default(FilterDefinition<UserMailEntity>), default, default);
        await _mail.ReceivedWithAnyArgs(1).AddAsync(default);
    }

    [Theory]
    [InlineData(UserMailKind.SupportAnswer)]
    [InlineData(UserMailKind.Receipt)]
    public void TheNewKinds_AreStoredByName(UserMailKind kind)
    {
        var document = MongoUserMailOutbox.ToRecordedEntity(Answer() with { Kind = kind }).ToBsonDocument();

        Assert.Equal(BsonType.String, document[nameof(UserMailEntity.Kind)].BsonType);
        Assert.Equal(kind.ToString(), document[nameof(UserMailEntity.Kind)].AsString);
    }

    [Fact]
    public void TheCaseReference_SurvivesTheRoundTripToStorage_AndANullCategoryReadsBack()
    {
        var item = MongoUserMailOutbox.ToItem(BsonSerializer.Deserialize<UserMailEntity>(MongoUserMailOutbox.ToRecordedEntity(Answer()).ToBsonDocument()));

        Assert.Equal(("case-1", (int?)2), (item.CaseId, item.CaseMessageSequence));
        Assert.Equal(UserMailKind.SupportAnswer, item.Mail.Kind);
        Assert.Null(item.Mail.Category);
        Assert.Equal(("staff-1", "Sam Staff"), (item.Mail.SentByUserKey, item.Mail.SentByName));
        Assert.Equal(("support@example.net", "Example Support", "case-1@example.net"), (item.FromAddress, item.FromName, item.ReplyTo));
        Assert.Equal("kund@customer.example", item.RecipientAddress);
    }

    [Fact]
    public void AMailOfNoCase_StoresNoCaseReference()
    {
        var document = MongoUserMailOutbox.ToEntity(new UserMail { RecipientUserKey = "u1", Category = "news", Subject = "S", Body = "B" }, Now).ToBsonDocument();

        Assert.False(document.Contains(nameof(UserMailEntity.CaseId)));
        Assert.False(document.Contains(nameof(UserMailEntity.CaseMessageSequence)));
    }

    [Theory]
    [InlineData(UserMailKind.SupportAnswer)]
    [InlineData(UserMailKind.Receipt)]
    public void TheLog_FiltersByTheNewKinds_ByName(UserMailKind kind)
    {
        var rendered = Render(MongoUserMailOutbox.ForLog(new UserMailFilter { Kind = kind }));

        Assert.Equal($"{{ \"Kind\" : \"{kind}\" }}", rendered);
    }

    [Fact]
    public void AUsersHistory_LeavesOutAnswersAndReceiptsToTheirAddress()
    {
        var rendered = Render(MongoUserMailOutbox.ForRecipient("user-1", "kund@customer.example"));

        Assert.Contains("\"Kind\" : { \"$nin\" : [\"SupportAnswer\", \"Receipt\"] }", rendered);
    }

    [Fact]
    public void ARecordedMail_IsFoundByItsCaseEntry_ThroughASparseIndex()
    {
        var collection = (UserMailCollection)RuntimeHelpers.GetUninitializedObject(typeof(UserMailCollection));
        typeof(UserMailCollection).GetField("_retention", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(collection, TimeSpan.FromDays(30));
        var index = collection.Indices.Single(x => x.Options.Name == "CaseId_CaseMessageSequence");
        var keys = index.Keys.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry));

        Assert.Equal(["CaseId", "CaseMessageSequence"], keys.Names);
        Assert.True(index.Options.Sparse);
    }

    [Fact]
    public async Task AnOutboxThatDoesNotRecord_KeepsNothing_AndSaysSo()
    {
        IUserMailOutbox minimal = new MinimalOutbox();

        Assert.Null(await minimal.RecordAsync(Answer(), TestContext.Current.CancellationToken));
    }

    private sealed class MinimalOutbox : IUserMailOutbox
    {
        public Task<UserMailQueued> AddAsync(UserMail mail, DateTime queuedAt, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<UserMailItem> ClaimNextAsync(string claimant, DateTime now, TimeSpan lease, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> MarkSentAsync(string id, string claimant, string messageId, string address, DateTime at, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> MarkSkippedAsync(string id, string claimant, string reason, DateTime at, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> MarkFailedAsync(string id, string claimant, string reason, DateTime at, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> PostponeAsync(string id, string claimant, string reason, DateTime nextAttemptAt, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<UserMailItem> GetAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
