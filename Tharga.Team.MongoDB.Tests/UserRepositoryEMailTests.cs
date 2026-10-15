using global::MongoDB.Bson;
using global::MongoDB.Bson.Serialization;
using global::MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// <see cref="IUserRepository{TUserEntity}.SetEMailAsync"/>: the built-in repository writes the address on the
/// user found by key, and a host's own repository that never implemented it is told so rather than ignored.
/// </summary>
public class UserRepositoryEMailTests
{
    [Fact]
    public async Task SetEMailAsync_UpdatesTheEMailField()
    {
        UpdateDefinition<UserEntity> captured = null;
        var collection = Substitute.For<IUserRepositoryCollection<UserEntity>>();
        await collection.UpdateOneAsync(
            Arg.Any<FilterDefinition<UserEntity>>(),
            Arg.Do<UpdateDefinition<UserEntity>>(x => captured = x));

        var sut = new UserRepository<UserEntity>(collection);
        await sut.SetEMailAsync("u-1", "anna@example.com");

        Assert.NotNull(captured);
        var rendered = captured.Render(Args()).AsBsonDocument;
        Assert.Equal("anna@example.com", rendered["$set"][nameof(IUser.EMail)].AsString);
    }

    [Fact]
    public async Task SetEMailAsync_TargetsTheUserByKey()
    {
        FilterDefinition<UserEntity> captured = null;
        var collection = Substitute.For<IUserRepositoryCollection<UserEntity>>();
        await collection.UpdateOneAsync(
            Arg.Do<FilterDefinition<UserEntity>>(x => captured = x),
            Arg.Any<UpdateDefinition<UserEntity>>());

        var sut = new UserRepository<UserEntity>(collection);
        await sut.SetEMailAsync("u-1", "anna@example.com");

        Assert.NotNull(captured);
        Assert.Equal("u-1", captured.Render(Args())[nameof(IUser.Key)].AsString);
    }

    [Fact]
    public async Task SetEMailAsync_OnARepositoryThatDoesNotImplementIt_Throws()
    {
        IUserRepository<UserEntity> sut = new HostRepository();

        var refused = await Assert.ThrowsAsync<NotSupportedException>(() => sut.SetEMailAsync("u-1", "anna@example.com"));

        Assert.Contains(nameof(IUserRepository<UserEntity>.SetEMailAsync), refused.Message);
        Assert.Contains(nameof(HostRepository), refused.Message);
    }

    private static RenderArgs<UserEntity> Args()
        => new(BsonSerializer.SerializerRegistry.GetSerializer<UserEntity>(), BsonSerializer.SerializerRegistry);

    public record UserEntity : EntityBase, IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
        public string Name { get; init; }
    }

    private sealed class HostRepository : IUserRepository<UserEntity>
    {
        public IAsyncEnumerable<UserEntity> GetAsync() => AsyncEnumerable.Empty<UserEntity>();
        public Task<UserEntity> GetAsync(string identity) => Task.FromResult<UserEntity>(null);
        public Task<UserEntity> GetByKeyAsync(string userKey) => Task.FromResult<UserEntity>(null);
        public Task AddAsync(UserEntity user) => Task.CompletedTask;
        public Task SetNameAsync(string userKey, string name) => Task.CompletedTask;
    }
}
