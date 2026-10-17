using System.Text.RegularExpressions;
using global::MongoDB.Bson;
using global::MongoDB.Bson.Serialization;
using global::MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// <see cref="IUserRepository{TUserEntity}.GetByEMailAsync"/>: every user holding an address, however the
/// sign-in provider spelled it, and nothing an address can smuggle into the query.
/// </summary>
public class UserRepositoryFindByEMailTests
{
    [Fact]
    public async Task TheBuiltInRepository_AsksForTheWholeAddressIgnoringCase()
    {
        var pattern = await RenderedPatternAsync("Anna@Example.com");

        Assert.Equal(@"^\s*Anna@Example\.com\s*$", pattern.Pattern);
        Assert.Contains("i", pattern.Options);
    }

    /// <summary>
    /// <b>Escaped, because an address is allowed characters a pattern treats as syntax.</b> Unescaped,
    /// <c>a+b@x.com</c> would match <c>aab@xycom</c>, and <c>.*@example.com</c> would match everybody there.
    /// </summary>
    [Fact]
    public async Task AnAddressWithPatternCharacters_MatchesOnlyItself()
    {
        var pattern = await RenderedPatternAsync("a+b.*@example.com");
        var regex = new Regex(pattern.Pattern, RegexOptions.IgnoreCase);

        Assert.Matches(regex, "a+b.*@example.com");
        Assert.Matches(regex, " A+B.*@EXAMPLE.COM ");
        Assert.DoesNotMatch(regex, "aab.x@example.com");
        Assert.DoesNotMatch(regex, "a+b.*@example.com.evil.org");
        Assert.DoesNotMatch(regex, "x.a+b.*@example.com");
    }

    [Fact]
    public async Task ABlankAddress_FindsNobodyWithoutAsking()
    {
        var collection = Substitute.For<IUserRepositoryCollection<UserEntity>>();

        Assert.Empty(await new UserRepository<UserEntity>(collection).GetByEMailAsync("  "));
        collection.DidNotReceive().GetAsync(Arg.Any<FilterDefinition<UserEntity>>());
    }

    /// <summary>A host's own repository keeps answering correctly through the default, if by reading every user.</summary>
    [Fact]
    public async Task AHostRepository_FindsEveryHolderTrimmedAndIgnoringCase()
    {
        IUserRepository<UserEntity> sut = new HostRepository(
            new UserEntity { Key = "1", Identity = "sub-1", EMail = " Anna@Example.com" },
            new UserEntity { Key = "2", Identity = "sub-2", EMail = "anna@example.com" },
            new UserEntity { Key = "3", Identity = "sub-3", EMail = "bert@example.com" },
            new UserEntity { Key = "4", Identity = "sub-4", EMail = null });

        var found = await sut.GetByEMailAsync("ANNA@example.com ");

        Assert.Equal(["1", "2"], found.Select(x => x.Key));
    }

    private static async Task<BsonRegularExpression> RenderedPatternAsync(string email)
    {
        FilterDefinition<UserEntity> captured = null;
        var collection = Substitute.For<IUserRepositoryCollection<UserEntity>>();
        collection.GetAsync(Arg.Do<FilterDefinition<UserEntity>>(x => captured = x)).Returns(AsyncEnumerable.Empty<UserEntity>());

        await new UserRepository<UserEntity>(collection).GetByEMailAsync(email);

        Assert.NotNull(captured);
        var rendered = captured.Render(new RenderArgs<UserEntity>(BsonSerializer.SerializerRegistry.GetSerializer<UserEntity>(), BsonSerializer.SerializerRegistry));

        return rendered[nameof(IUser.EMail)].AsBsonRegularExpression;
    }

    public record UserEntity : EntityBase, IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
    }

    private sealed class HostRepository(params UserEntity[] users) : IUserRepository<UserEntity>
    {
        public IAsyncEnumerable<UserEntity> GetAsync() => users.ToAsyncEnumerable();
        public Task<UserEntity> GetAsync(string identity) => Task.FromResult<UserEntity>(null);
        public Task<UserEntity> GetByKeyAsync(string userKey) => Task.FromResult<UserEntity>(null);
        public Task AddAsync(UserEntity user) => Task.CompletedTask;
        public Task SetNameAsync(string userKey, string name) => Task.CompletedTask;
    }
}
