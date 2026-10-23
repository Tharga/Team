using System.Reflection;
using System.Runtime.CompilerServices;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The MongoDB side of the global mail log: the filter, newest first by id after the cursor, the bodies and files left on the
/// server, a team recorded on every mail and indexed, and a mail stored before kinds were counted as the kind it is read as.
/// </summary>
public class UserMailLogStoreTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    private readonly IUserMailCollection _mail = Substitute.For<IUserMailCollection>();
    private readonly IUserMailBatchCollection _batches = Substitute.For<IUserMailBatchCollection>();

    private static string Render(FilterDefinition<UserMailEntity> filter)
        => filter.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).ToString();

    private static string Render(ProjectionDefinition<UserMailEntity> projection)
        => projection.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).ToString();

    private static string Render(SortDefinition<UserMailEntity> sort)
        => sort.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).ToString();

    private static string RenderLog(UserMailFilter filter) => Render(MongoUserMailOutbox.ForLog(filter));

    [Fact]
    public void WithNoFilter_EveryMailIsSelected()
    {
        Assert.Equal("{ }", RenderLog(null));
        Assert.Equal("{ }", RenderLog(new UserMailFilter()));
    }

    [Fact]
    public void EachPartGiven_NarrowsTheSelection()
    {
        var rendered = RenderLog(new UserMailFilter
        {
            Kind = UserMailKind.Notice,
            Category = "news",
            Status = UserMailStatus.Failed,
            From = Now,
            To = Now.AddDays(1),
            RecipientUserKey = "user-1",
            RecipientAddress = "anna@example.org",
            TeamKey = "acme"
        });

        Assert.Contains("\"Kind\" : \"Notice\"", rendered);
        Assert.Contains("\"Category\" : \"news\"", rendered);
        Assert.Contains("\"Status\" : \"Failed\"", rendered);
        Assert.Contains("\"QueuedAt\" : { \"$gte\" :", rendered);
        Assert.Contains("\"$lt\" :", rendered);
        Assert.Contains("\"RecipientUserKey\" : \"user-1\"", rendered);
        Assert.Contains("\"RecipientAddress\" : \"anna@example.org\"", rendered);
        Assert.Contains("\"TeamKey\" : \"acme\"", rendered);
    }

    [Fact]
    public void AMessageOrMail_AlsoSelectsWhatWasStoredBeforeKindsWere_ByWhetherItNamesABatch()
    {
        var message = RenderLog(new UserMailFilter { Kind = UserMailKind.Message });
        var mail = RenderLog(new UserMailFilter { Kind = UserMailKind.Mail });

        Assert.Contains("$or", message);
        Assert.Contains("\"Kind\" : \"Message\"", message);
        Assert.Contains("\"BatchId\" : { \"$ne\" : null }", message);
        Assert.Contains("$or", mail);
        Assert.Contains("\"Kind\" : \"Mail\"", mail);
        Assert.Contains("\"BatchId\" : null", mail);
    }

    [Theory]
    [InlineData(UserMailKind.Notice)]
    [InlineData(UserMailKind.Invitation)]
    [InlineData(UserMailKind.Contact)]
    public void AnyOtherKind_IsSelectedByName_Alone(UserMailKind kind)
    {
        var rendered = RenderLog(new UserMailFilter { Kind = kind });

        Assert.DoesNotContain("$or", rendered);
        Assert.Contains($"\"Kind\" : \"{kind}\"", rendered);
    }

    [Fact]
    public async Task TheLog_IsNewestFirst_AfterTheCursor_WithoutBodiesOrFiles()
    {
        FilterDefinition<UserMailEntity> filter = null;
        Options<UserMailEntity> options = null;
        _mail.GetManyAsync(Arg.Do<FilterDefinition<UserMailEntity>>(x => filter = x), Arg.Do<Options<UserMailEntity>>(x => options = x), Arg.Any<CancellationToken>())
            .Returns(new Result<UserMailEntity, ObjectId> { Items = [] });
        var before = ObjectId.GenerateNewId().ToString();

        await new MongoUserMailOutbox(_mail, _batches).ListAsync(new UserMailFilter { TeamKey = "acme" }, before, 50, TestContext.Current.CancellationToken);

        var rendered = Render(filter);
        Assert.Contains("\"TeamKey\" : \"acme\"", rendered);
        Assert.Contains($"\"_id\" : {{ \"$lt\" : {{ \"$oid\" : \"{before}\" }} }}", rendered);
        Assert.Equal("{ \"_id\" : -1 }", Render(options.Sort));
        Assert.Equal(["Attachments.Content", "Body", "BodyHtml"], BsonDocument.Parse(Render(options.Projection)).Names.Order());
        Assert.Equal(51, options.Limit);
    }

    [Fact]
    public async Task ThePageAfterTheLastItem_HasACursor_AndTheLastPageHasNone()
    {
        var stored = Enumerable.Range(0, 3).Select(i => MongoUserMailOutbox.ToEntity(new UserMail { RecipientUserKey = $"u{i}", Category = "news", Subject = "S" }, Now)).ToArray();
        _mail.GetManyAsync(Arg.Any<FilterDefinition<UserMailEntity>>(), Arg.Any<Options<UserMailEntity>>(), Arg.Any<CancellationToken>())
            .Returns(new Result<UserMailEntity, ObjectId> { Items = stored });
        var outbox = new MongoUserMailOutbox(_mail, _batches);

        var first = await outbox.ListAsync(null, null, 2, TestContext.Current.CancellationToken);
        var last = await outbox.ListAsync(null, null, 3, TestContext.Current.CancellationToken);

        Assert.Equal(2, first.Items.Length);
        Assert.Equal(stored[1].Id.ToString(), first.NextCursor);
        Assert.Equal(3, last.Items.Length);
        Assert.Null(last.NextCursor);
    }

    [Fact]
    public async Task ACursorNotIssuedByTheOutbox_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new MongoUserMailOutbox(_mail, _batches).ListAsync(null, "not-a-cursor", 10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TheTeam_IsStoredOnTheMail_AndReadBack_OnlyWhenThereIsOne()
    {
        var withTeam = MongoUserMailOutbox.ToEntity(new UserMail { RecipientUserKey = "u", Category = "news", Subject = "S", TeamKey = "acme" }, Now);
        var without = MongoUserMailOutbox.ToEntity(new UserMail { RecipientUserKey = "u", Category = "news", Subject = "S" }, Now);

        Assert.Equal("acme", withTeam.ToBsonDocument()[nameof(UserMailEntity.TeamKey)].AsString);
        Assert.False(without.ToBsonDocument().Contains(nameof(UserMailEntity.TeamKey)));
        Assert.Equal("acme", MongoUserMailOutbox.ToItem(withTeam).Mail.TeamKey);
        Assert.Null(MongoUserMailOutbox.ToItem(without).Mail.TeamKey);
    }

    [Fact]
    public void EveryMailOfABatch_CarriesTheBatchsTeam_UsersAndAddressesAlike()
    {
        var batch = new UserMailBatch
        {
            RecipientUserKeys = ["a", "b"],
            RecipientAddresses = [new UserMailBatchAddress("c@example.org", "Cora")],
            Category = "news",
            Subject = "S",
            Body = "B",
            TeamKey = "acme"
        };

        var mails = MongoUserMailOutbox.ToBatchItems("batch-1", batch, Now).ToArray();

        Assert.Equal(3, mails.Length);
        Assert.All(mails, x => Assert.Equal("acme", x.TeamKey));
    }

    [Fact]
    public void AMailOfATeam_IsFoundByTheTeamNewestFirst_ThroughASparseIndex()
    {
        var collection = (UserMailCollection)RuntimeHelpers.GetUninitializedObject(typeof(UserMailCollection));
        typeof(UserMailCollection).GetField("_retention", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(collection, TimeSpan.FromDays(30));
        var index = collection.Indices.Single(x => x.Options.Name == "TeamKey_Id");
        var keys = index.Keys.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry));

        Assert.Equal(["TeamKey", "_id"], keys.Names);
        Assert.Equal(-1, keys["_id"].AsInt32);
        Assert.True(index.Options.Sparse);
    }
}
