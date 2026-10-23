using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The MongoDB side of recording a refused mail: stored as given up and never attempted, with its completion time so it expires
/// like any done mail, once per cause, and kept out of a user's own history while the log still lists it.
/// </summary>
public class UserMailRefusalStoreTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    private readonly IUserMailCollection _mail = Substitute.For<IUserMailCollection>();
    private readonly IUserMailBatchCollection _batches = Substitute.For<IUserMailBatchCollection>();

    private static readonly UserMail Refused = new() { RecipientUserKey = "user-1", Category = "nope", Subject = "Hello", TeamKey = "acme", Kind = UserMailKind.Notice };

    private static string Render(FilterDefinition<UserMailEntity> filter)
        => filter.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).ToString();

    private static MongoWriteException DuplicateKey()
    {
        var ex = (MongoWriteException)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MongoWriteException));
        var writeError = (WriteError)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(WriteError));

        typeof(WriteError)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(ServerErrorCategory))
            .SetValue(writeError, ServerErrorCategory.DuplicateKey);
        typeof(MongoWriteException)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(WriteError))
            .SetValue(ex, writeError);

        return ex;
    }

    [Fact]
    public async Task ARefusedMail_IsStoredAsGivenUp_NeverAttempted_AndDoneSoItExpires()
    {
        UserMailEntity added = null;
        await _mail.AddAsync(Arg.Do<UserMailEntity>(x => added = x));

        var recorded = await new MongoUserMailOutbox(_mail, _batches).AddRefusedAsync(Refused, "Not a category.", "refused:key", Now, TestContext.Current.CancellationToken);

        Assert.True(recorded);
        Assert.Equal((UserMailStatus.Failed, 0, Now, Now, "Not a category.", true), (added.Status, added.Attempts, added.QueuedAt, added.CompletedAt, added.Reason, added.Refused));
        Assert.Equal(("user-1", "nope", "Hello", "acme", "refused:key", UserMailKind.Notice), (added.RecipientUserKey, added.Category, added.Subject, added.TeamKey, added.DeduplicationKey, added.Kind));
        Assert.Null(added.ClaimedBy);
    }

    [Fact]
    public async Task ARefusalAlreadyRecorded_IsNotRecordedAgain_AndSaysSo()
    {
        _mail.AddAsync(Arg.Any<UserMailEntity>()).Returns<Task>(_ => throw DuplicateKey());

        var recorded = await new MongoUserMailOutbox(_mail, _batches).AddRefusedAsync(Refused, "Not a category.", "refused:key", Now, TestContext.Current.CancellationToken);

        Assert.False(recorded);
    }

    [Fact]
    public async Task AnyOtherFailureToStore_IsNotSwallowed()
    {
        _mail.AddAsync(Arg.Any<UserMailEntity>()).Returns<Task>(_ => throw new TimeoutException("The database is down."));

        await Assert.ThrowsAsync<TimeoutException>(() => new MongoUserMailOutbox(_mail, _batches).AddRefusedAsync(Refused, "Not a category.", "refused:key", Now, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TheFlag_IsStoredOnlyWhenSet_AndReadBack()
    {
        var refused = MongoUserMailOutbox.ToEntity(Refused, Now) with { Refused = true };
        var ordinary = MongoUserMailOutbox.ToEntity(Refused, Now);

        Assert.True(refused.ToBsonDocument()[nameof(UserMailEntity.Refused)].AsBoolean);
        Assert.False(ordinary.ToBsonDocument().Contains(nameof(UserMailEntity.Refused)));
        Assert.True(MongoUserMailOutbox.ToItem(refused).Refused);
        Assert.False(MongoUserMailOutbox.ToItem(ordinary).Refused);
    }

    [Fact]
    public void AUsersOwnHistory_LeavesRefusalsOut_AndAMailWithNoFlagStillCounts()
    {
        var rendered = Render(MongoUserMailOutbox.ForRecipient("user-1", "anna@example.org"));

        Assert.Contains("\"Refused\" : { \"$ne\" : true }", rendered);
        Assert.Contains("\"RecipientUserKey\" : \"user-1\"", rendered);
    }

    [Fact]
    public void TheLog_DoesNotLeaveThemOut()
    {
        Assert.DoesNotContain("Refused", Render(MongoUserMailOutbox.ForLog(new UserMailFilter { Status = UserMailStatus.Failed })));
    }
}
