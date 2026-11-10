using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.MongoDB;

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

    [Fact]
    public async Task ARefusedMail_IsStoredAsGivenUp_NeverAttempted_AndDoneSoItExpires()
    {
        UserMailEntity added = null;
        _mail.TryAddAsync(Arg.Do<UserMailEntity>(x => added = x), Arg.Any<IClientSessionHandle>()).Returns(true);

        var recorded = await new MongoUserMailOutbox(_mail, _batches).AddRefusedAsync(Refused, "Not a category.", "refused:key", Now, TestContext.Current.CancellationToken);

        Assert.True(recorded);
        Assert.Equal((UserMailStatus.Failed, 0, Now, Now, "Not a category.", true), (added.Status, added.Attempts, added.QueuedAt, added.CompletedAt, added.Reason, added.Refused));
        Assert.Equal(("user-1", "nope", "Hello", "acme", "refused:key", UserMailKind.Notice), (added.RecipientUserKey, added.Category, added.Subject, added.TeamKey, added.DeduplicationKey, added.Kind));
        Assert.Null(added.ClaimedBy);
    }

    [Fact]
    public async Task ARefusalAlreadyRecorded_IsNotRecordedAgain_AndSaysSo()
    {
        // The insert answers false (the key is taken, and the collection logs nothing) and the earlier refusal is there to find.
        _mail.TryAddAsync(Arg.Any<UserMailEntity>(), Arg.Any<IClientSessionHandle>()).Returns(false);
        _mail.GetOneAsync(Arg.Any<System.Linq.Expressions.Expression<Func<UserMailEntity, bool>>>(), Arg.Any<OneOption<UserMailEntity>>(), Arg.Any<CancellationToken>())
            .Returns(MongoUserMailOutbox.ToEntity(Refused, Now));

        var recorded = await new MongoUserMailOutbox(_mail, _batches).AddRefusedAsync(Refused, "Not a category.", "refused:key", Now, TestContext.Current.CancellationToken);

        Assert.False(recorded);
    }

    [Fact]
    public async Task AFailureToStore_WithNoEarlierRefusalToFind_IsNotSwallowed()
    {
        // The collection answers false for any write error, so a refusal that is not there means the write failed.
        _mail.TryAddAsync(Arg.Any<UserMailEntity>(), Arg.Any<IClientSessionHandle>()).Returns(false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new MongoUserMailOutbox(_mail, _batches).AddRefusedAsync(Refused, "Not a category.", "refused:key", Now, TestContext.Current.CancellationToken));
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
