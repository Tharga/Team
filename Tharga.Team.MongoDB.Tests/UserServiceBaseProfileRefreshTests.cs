using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The opt-in profile refresh <see cref="UserServiceBase.GetCurrentUserAsync"/> performs on every resolve: a host
/// that trusts a sign-in has the stored name and email follow its claims. The attempt guard is static (per process),
/// so every test uses unique identities and user keys.
/// </summary>
public class UserServiceBaseProfileRefreshTests
{
    private const string StoredName = "Anna Andersson";
    private const string StoredEMail = "anna@example.com";

    [Fact]
    public async Task NotOptedIn_NeverWritesTheProfile()
    {
        var built = Build(optIn: false, name: "Anna Svensson", email: "anna.svensson@example.com");

        await built.Sut.GetCurrentUserAsync(built.Principal);

        Assert.Empty(built.Sut.Written);
    }

    [Fact]
    public async Task OptedIn_ADifferentName_IsWrittenAndTheEMailLeftAlone()
    {
        var built = Build(name: "Anna Svensson", email: StoredEMail);

        await built.Sut.GetCurrentUserAsync(built.Principal);

        Assert.Equal([(built.Key, "Anna Svensson", (string)null)], built.Sut.Written);
    }

    [Fact]
    public async Task OptedIn_ADifferentEMail_IsWrittenAndTheNameLeftAlone()
    {
        var built = Build(name: StoredName, email: "anna.svensson@example.com");

        await built.Sut.GetCurrentUserAsync(built.Principal);

        Assert.Equal([(built.Key, (string)null, "anna.svensson@example.com")], built.Sut.Written);
    }

    [Fact]
    public async Task OptedIn_TheSameValues_AreNotWritten()
    {
        var built = Build(name: StoredName, email: StoredEMail);

        await built.Sut.GetCurrentUserAsync(built.Principal);

        Assert.Empty(built.Sut.Written);
    }

    [Fact]
    public async Task OptedIn_AnEMailDifferingOnlyInCase_IsNotWritten()
    {
        var built = Build(name: StoredName, email: "Anna@Example.com");

        await built.Sut.GetCurrentUserAsync(built.Principal);

        Assert.Empty(built.Sut.Written);
    }

    [Fact]
    public async Task OptedIn_NoNameOrEMailClaims_OverwriteNothing()
    {
        var built = Build(name: null, email: null);

        await built.Sut.GetCurrentUserAsync(built.Principal);

        Assert.Empty(built.Sut.Written);
    }

    [Fact]
    public async Task OptedIn_ADisplayNameThatIsOnlyTheEMailFallback_IsNotWrittenAsTheName()
    {
        var built = Build(name: null, email: "anna.svensson@example.com");

        await built.Sut.GetCurrentUserAsync(built.Principal);

        Assert.Equal([(built.Key, (string)null, "anna.svensson@example.com")], built.Sut.Written);
    }

    [Fact]
    public async Task OptedIn_AStoreThatDidNotKeepTheChange_IsNotWrittenAgainForTheSameValues()
    {
        var built = Build(name: "Anna Svensson", email: StoredEMail);

        await built.Sut.GetCurrentUserAsync(built.Principal);
        await built.Sut.GetCurrentUserAsync(built.Principal);

        Assert.Single(built.Sut.Written);
    }

    [Fact]
    public async Task OptedIn_ALaterDifferentValue_IsWrittenAgain()
    {
        var built = Build(name: "Anna Svensson", email: StoredEMail);
        await built.Sut.GetCurrentUserAsync(built.Principal);

        await built.Sut.GetCurrentUserAsync(Principal(built.Identity, "Anna Berg", StoredEMail));

        Assert.Equal(2, built.Sut.Written.Count);
        Assert.Equal("Anna Berg", built.Sut.Written[1].Name);
    }

    [Fact]
    public async Task OptedIn_AWrite_DropsTheUsersCacheEntry()
    {
        var built = Build(name: "Anna Svensson", email: StoredEMail);

        await built.Sut.GetCurrentUserAsync(built.Principal);

        await built.Cache.Received().RemoveUserAsync(built.Identity);
    }

    [Fact]
    public async Task OptedIn_AWrite_RaisesTheEventWithTheStoredUserAndTheNewValues()
    {
        var built = Build(name: "Anna Svensson", email: "anna.svensson@example.com");
        UserProfileRefreshedEventArgs raised = null;
        built.Sut.UserProfileRefreshedEvent += (_, e) => raised = e;

        await built.Sut.GetCurrentUserAsync(built.Principal);

        Assert.NotNull(raised);
        Assert.Equal(built.Key, raised.User.Key);
        Assert.Equal(StoredName, raised.User.Name);
        Assert.Equal("Anna Svensson", raised.Name);
        Assert.Equal("anna.svensson@example.com", raised.EMail);
        Assert.Same(built.Principal, raised.Principal);
    }

    [Fact]
    public async Task OptedIn_NoWrite_RaisesNoEvent()
    {
        var built = Build(name: StoredName, email: StoredEMail);
        var raised = false;
        built.Sut.UserProfileRefreshedEvent += (_, _) => raised = true;

        await built.Sut.GetCurrentUserAsync(built.Principal);

        Assert.False(raised);
    }

    [Fact]
    public async Task OptedIn_AFailingWrite_StillResolvesTheUser()
    {
        var built = Build(name: "Anna Svensson", email: StoredEMail);
        built.Sut.Fail = true;

        var user = await built.Sut.GetCurrentUserAsync(built.Principal);

        Assert.NotNull(user);
        Assert.Equal(built.Key, user.Key);
    }

    [Fact]
    public async Task OptedIn_OnAServiceThatNeverImplementedTheWrite_StillResolvesTheUser()
    {
        var identity = $"id-{Guid.NewGuid():N}";
        var user = new TestUserEntity { Identity = identity, Key = $"u-{Guid.NewGuid():N}", Name = StoredName, EMail = StoredEMail };
        var sut = new UnimplementedProfileService(user);

        var resolved = await sut.GetCurrentUserAsync(Principal(identity, "Anna Svensson", StoredEMail));

        Assert.Same(user, resolved);
    }

    private static Built Build(bool optIn = true, string name = null, string email = null)
    {
        var identity = $"id-{Guid.NewGuid():N}";
        var key = $"u-{Guid.NewGuid():N}";
        var entity = new TestUserEntity { Identity = identity, Key = key, Name = StoredName, EMail = StoredEMail };

        var repo = Substitute.For<IUserRepository<TestUserEntity>>();
        repo.GetAsync(identity).Returns(entity);
        repo.GetByKeyAsync(key).Returns(entity);

        var cache = Substitute.For<ITeamCache>();
        var sut = new TestUserService(repo, cache) { OptIn = optIn };

        return new Built(sut, cache, Principal(identity, name, email), identity, key);
    }

    private static ClaimsPrincipal Principal(string identity, string name, string email)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, identity) };
        if (name != null) claims.Add(new Claim("name", name));
        if (email != null) claims.Add(new Claim("email", email));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private sealed record Built(TestUserService Sut, ITeamCache Cache, ClaimsPrincipal Principal, string Identity, string Key);

    public record TestUserEntity : EntityBase, IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
        public string Name { get; init; }
    }

    private sealed class TestUserService : UserServiceRepositoryBase<TestUserEntity>
    {
        public TestUserService(IUserRepository<TestUserEntity> repo, ITeamCache cache)
            : base(Substitute.For<AuthenticationStateProvider>(), repo, cache: cache)
        {
        }

        public bool OptIn { get; init; }

        public bool Fail { get; set; }

        public List<(string Key, string Name, string EMail)> Written { get; } = [];

        protected override bool ShouldRefreshProfile(ClaimsPrincipal claimsPrincipal) => OptIn;

        protected override Task SetUserProfileAsync(string userKey, string name, string email)
        {
            if (Fail) throw new InvalidOperationException("store down");

            Written.Add((userKey, name, email));
            return Task.CompletedTask;
        }

        protected override TimeSpan? LastSeenStampInterval => null;

        protected override Task<TestUserEntity> CreateUserEntityAsync(ClaimsPrincipal claimsPrincipal, string identity)
            => Task.FromResult(new TestUserEntity { Identity = identity, Key = Guid.NewGuid().ToString() });
    }

    private sealed class UnimplementedProfileService : UserServiceBase
    {
        private readonly TestUserEntity _user;

        public UnimplementedProfileService(TestUserEntity user)
            : base(Substitute.For<AuthenticationStateProvider>(), cache: Substitute.For<ITeamCache>())
        {
            _user = user;
        }

        protected override bool ShouldRefreshProfile(ClaimsPrincipal claimsPrincipal) => true;

        protected override TimeSpan? LastSeenStampInterval => null;

        protected override Task<IUser> GetUserAsync(ClaimsPrincipal claimsPrincipal) => Task.FromResult<IUser>(_user);

        protected override IAsyncEnumerable<IUser> GetAllAsync() => new[] { (IUser)_user }.ToAsyncEnumerable();
    }
}
