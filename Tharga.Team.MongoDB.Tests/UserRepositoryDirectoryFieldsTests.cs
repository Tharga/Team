using System.Linq.Expressions;
using global::MongoDB.Bson;
using global::MongoDB.Bson.Serialization;
using global::MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// Verifies the opt-in-by-entity-shape contract on <see cref="UserRepository{TUserEntity}"/>:
/// <c>LastSeen</c>/<c>DirectoryId</c> writes only run when the entity declares the property (updating
/// an undeclared interface default member would fail at driver render time), and that the update
/// expressions — which bind to the <see cref="IUser"/> interface members via the generic constraint —
/// render correctly against the entity's class map.
/// </summary>
public class UserRepositoryDirectoryFieldsTests
{
    [Fact]
    public async Task SetLastSeenAsync_EntityWithoutProperty_DoesNotUpdate()
    {
        var collection = Substitute.For<IUserRepositoryCollection<PlainUserEntity>>();
        var sut = new UserRepository<PlainUserEntity>(collection);

        await sut.SetLastSeenAsync("u-1", DateTime.UtcNow);

        await collection.DidNotReceiveWithAnyArgs()
            .UpdateOneAsync(default(FilterDefinition<PlainUserEntity>), default(UpdateDefinition<PlainUserEntity>));
    }

    [Fact]
    public async Task SetDirectoryIdAsync_EntityWithoutProperty_DoesNotUpdate()
    {
        var collection = Substitute.For<IUserRepositoryCollection<PlainUserEntity>>();
        var sut = new UserRepository<PlainUserEntity>(collection);

        await sut.SetDirectoryIdAsync("u-1", "oid-1");

        await collection.DidNotReceiveWithAnyArgs()
            .UpdateOneAsync(default(FilterDefinition<PlainUserEntity>), default(UpdateDefinition<PlainUserEntity>));
    }

    [Fact]
    public async Task SetLastSeenAsync_EntityWithProperty_UpdatesLastSeenField()
    {
        UpdateDefinition<TrackedUserEntity> captured = null;
        var collection = Substitute.For<IUserRepositoryCollection<TrackedUserEntity>>();
        await collection.UpdateOneAsync(
            Arg.Any<FilterDefinition<TrackedUserEntity>>(),
            Arg.Do<UpdateDefinition<TrackedUserEntity>>(x => captured = x));

        var sut = new UserRepository<TrackedUserEntity>(collection);
        var lastSeen = new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc);
        await sut.SetLastSeenAsync("u-1", lastSeen);

        Assert.NotNull(captured);
        var rendered = Render(captured);
        Assert.True(rendered.Contains("$max"), $"Expected a $max update, got: {rendered}");
        Assert.True(rendered["$max"].AsBsonDocument.Contains(nameof(IUser.LastSeen)), $"Expected LastSeen in: {rendered}");
    }

    [Fact]
    public async Task SetDirectoryIdAsync_EntityWithProperty_UpdatesDirectoryIdField()
    {
        UpdateDefinition<TrackedUserEntity> captured = null;
        var collection = Substitute.For<IUserRepositoryCollection<TrackedUserEntity>>();
        await collection.UpdateOneAsync(
            Arg.Any<FilterDefinition<TrackedUserEntity>>(),
            Arg.Do<UpdateDefinition<TrackedUserEntity>>(x => captured = x));

        var sut = new UserRepository<TrackedUserEntity>(collection);
        await sut.SetDirectoryIdAsync("u-1", "oid-1");

        Assert.NotNull(captured);
        var rendered = Render(captured);
        Assert.True(rendered["$set"].AsBsonDocument.Contains(nameof(IUser.DirectoryId)), $"Expected DirectoryId in: {rendered}");
        Assert.Equal("oid-1", rendered["$set"][nameof(IUser.DirectoryId)].AsString);
    }

    [Fact]
    public async Task DeleteAsync_DeletesByKey()
    {
        var collection = Substitute.For<IUserRepositoryCollection<TrackedUserEntity>>();
        var sut = new UserRepository<TrackedUserEntity>(collection);

        await sut.DeleteAsync("u-1");

        await collection.ReceivedWithAnyArgs(1).DeleteOneAsync(default(Expression<Func<TrackedUserEntity, bool>>));
    }

    private static BsonDocument Render(UpdateDefinition<TrackedUserEntity> update)
    {
        var serializer = BsonSerializer.SerializerRegistry.GetSerializer<TrackedUserEntity>();
        return update.Render(new RenderArgs<TrackedUserEntity>(serializer, BsonSerializer.SerializerRegistry)).AsBsonDocument;
    }

    public record PlainUserEntity : EntityBase, IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
    }

    public record TrackedUserEntity : EntityBase, IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
        public string Name { get; init; }
        public string DirectoryId { get; init; }
        public DateTime? LastSeen { get; init; }
    }
}
