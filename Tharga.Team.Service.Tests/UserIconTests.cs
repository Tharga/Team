using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// User icons: the built-in <see cref="GravatarIconSource"/> (users with email → Gravatar; teams/no-email
/// → null) and the self-service <see cref="UserServiceBase.SetOwnIconAsync"/> / <c>ClearOwnIconAsync</c>
/// orchestration (store → persist reference → delete previous; NotSupported without a store; Unauthorized
/// without a current user).
/// </summary>
public class UserIconTests
{
    // ---- GravatarIconSource ----

    [Fact]
    public async Task Gravatar_UserWithEmail_ReturnsGravatarUrl()
    {
        var subject = new IconSubject { Kind = IconKind.User, Key = "u1", EMail = "Test@Example.com" };
        var image = await new GravatarIconSource().ResolveAsync(subject);

        Assert.NotNull(image);
        // md5 of the trimmed lower-cased email.
        Assert.Contains("gravatar.com/avatar/55502f40dc8b7c769880b10874abc9d0", image.Url);
    }

    /// <summary>The default style is one Gravatar actually understands.</summary>
    /// <remarks>
    /// The set used to live in prose, so a default outside it would have been found by an avatar rendering
    /// wrong rather than by anything failing. This is the check that makes <see cref="GravatarStyles"/> worth
    /// having beyond tidiness.
    /// </remarks>
    [Fact]
    public void GravatarStyles_DefaultIsAKnownStyle()
    {
        Assert.True(GravatarStyles.IsKnown(GravatarStyles.Default));
        Assert.Equal(GravatarStyles.All.Distinct(), GravatarStyles.All);
    }

    /// <summary>
    /// A preview forces the default image, or it shows the caller's own photo for every style.
    /// </summary>
    [Fact]
    public void GravatarPreview_ForcesTheDefaultImage_SoTheStyleIsWhatRenders()
    {
        var preview = GravatarIconSource.PreviewUrl("Test@Example.com", GravatarStyles.RoboHash);

        Assert.Contains("d=robohash", preview);
        Assert.Contains("f=y", preview);
        Assert.DoesNotContain("f=y", GravatarIconSource.AvatarUrl("Test@Example.com", GravatarStyles.RoboHash));
    }

    /// <summary>A blank or missing style resolves to the default rather than producing <c>d=</c>.</summary>
    [Fact]
    public void GravatarUrl_BlankStyle_FallsBackToTheDefault()
    {
        Assert.Contains($"d={GravatarStyles.Default}", GravatarIconSource.AvatarUrl("a@b.c", null));
        Assert.Contains($"d={GravatarStyles.Default}", GravatarIconSource.AvatarUrl("a@b.c", "  "));
        Assert.Null(GravatarIconSource.AvatarUrl(null, GravatarStyles.Retro));
    }

    [Fact]
    public async Task Gravatar_Team_ReturnsNull()
    {
        var subject = new IconSubject { Kind = IconKind.Team, Key = "t1", EMail = "team@example.com" };
        Assert.Null(await new GravatarIconSource().ResolveAsync(subject));
    }

    [Fact]
    public async Task Gravatar_UserWithoutEmail_ReturnsNull()
    {
        var subject = new IconSubject { Kind = IconKind.User, Key = "u1" };
        Assert.Null(await new GravatarIconSource().ResolveAsync(subject));
    }

    [Fact]
    public async Task Gravatar_Disabled_ReturnsNull()
    {
        var source = new GravatarIconSource(new IconSettings { GravatarEnabled = false });
        var subject = new IconSubject { Kind = IconKind.User, Key = "u1", EMail = "a@b.c" };
        Assert.Null(await source.ResolveAsync(subject));
    }

    [Fact]
    public async Task Gravatar_UsesConfiguredStyle()
    {
        var source = new GravatarIconSource(new IconSettings { GravatarStyle = "robohash" });
        var subject = new IconSubject { Kind = IconKind.User, Key = "u1", EMail = "a@b.c" };
        var image = await source.ResolveAsync(subject);
        Assert.Contains("d=robohash", image.Url);
    }

    // ---- DefaultIconSource ----

    [Fact]
    public async Task Default_UserWithConfiguredUrl_ReturnsIt()
    {
        var source = new DefaultIconSource(new IconSettings { DefaultUserIconUrl = "https://x/default.png" });
        var image = await source.ResolveAsync(new IconSubject { Kind = IconKind.User, Key = "u1" });
        Assert.Equal("https://x/default.png", image.Url);
    }

    [Fact]
    public async Task Default_NoUrl_ReturnsNull()
    {
        var source = new DefaultIconSource(new IconSettings());
        Assert.Null(await source.ResolveAsync(new IconSubject { Kind = IconKind.User, Key = "u1" }));
    }

    [Fact]
    public async Task Default_Team_ReturnsNull()
    {
        var source = new DefaultIconSource(new IconSettings { DefaultUserIconUrl = "https://x/default.png" });
        Assert.Null(await source.ResolveAsync(new IconSubject { Kind = IconKind.Team, Key = "t1" }));
    }

    // ---- Self-service orchestration ----

    private sealed record TestUser : IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
        public string Icon { get; init; }
    }

    private sealed class FakeAsp(string identity) : AuthenticationStateProvider
    {
        private readonly ClaimsPrincipal _principal = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, identity)], "test"));
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(_principal));
    }

    private sealed class IconTestUserService : UserServiceBase
    {
        private readonly IUser _current;
        public string SetReference { get; private set; }
        public bool SetReferenceCalled { get; private set; }

        public IconTestUserService(IIconStore store, IUser current, string identity)
            : base(new FakeAsp(identity), iconStore: store)
        {
            _current = current;
        }

        protected override TimeSpan? LastSeenStampInterval => null;
        protected override Task<IUser> GetUserAsync(ClaimsPrincipal claimsPrincipal) => Task.FromResult(_current);
        protected override async IAsyncEnumerable<IUser> GetAllAsync() { yield break; }
        public override Task<IUser> GetUserByKeyAsync(string userKey) => Task.FromResult(_current?.Key == userKey ? _current : null);

        protected override Task SetUserIconReferenceAsync(string userKey, string reference)
        {
            SetReferenceCalled = true;
            SetReference = reference;
            return Task.CompletedTask;
        }
    }

    private static (IconTestUserService Sut, IIconStore Store) Build(IUser current)
    {
        var identity = $"id-{Guid.NewGuid():N}";
        current = ((TestUser)current) with { Identity = identity };
        var store = Substitute.For<IIconStore>();
        return (new IconTestUserService(store, current, identity), store);
    }

    [Fact]
    public async Task SetOwnIcon_StoresPersistsReference_DeletesPrevious()
    {
        var (sut, store) = Build(new TestUser { Key = "u1", EMail = "a@b.c", Icon = "old-ref" });
        store.SaveAsync(IconKind.User, "u1", Arg.Any<byte[]>(), "image/png").Returns("new-ref");

        await sut.SetOwnIconAsync([1, 2], "image/png");

        Assert.Equal("new-ref", sut.SetReference);
        await store.Received(1).SaveAsync(IconKind.User, "u1", Arg.Any<byte[]>(), "image/png");
        await store.Received(1).DeleteAsync("old-ref");
    }

    [Fact]
    public async Task SetOwnIcon_NoPrevious_DoesNotDelete()
    {
        var (sut, store) = Build(new TestUser { Key = "u1", EMail = "a@b.c", Icon = null });
        store.SaveAsync(IconKind.User, "u1", Arg.Any<byte[]>(), "image/png").Returns("new-ref");

        await sut.SetOwnIconAsync([1], "image/png");

        Assert.Equal("new-ref", sut.SetReference);
        await store.DidNotReceiveWithAnyArgs().DeleteAsync(default);
    }

    [Fact]
    public async Task ClearOwnIcon_WithIcon_ClearsAndDeletes()
    {
        var (sut, store) = Build(new TestUser { Key = "u1", EMail = "a@b.c", Icon = "ref-1" });

        await sut.ClearOwnIconAsync();

        Assert.True(sut.SetReferenceCalled);
        Assert.Null(sut.SetReference);
        await store.Received(1).DeleteAsync("ref-1");
    }

    [Fact]
    public async Task ClearOwnIcon_NoIcon_NoOp()
    {
        var (sut, store) = Build(new TestUser { Key = "u1", EMail = "a@b.c", Icon = null });

        await sut.ClearOwnIconAsync();

        Assert.False(sut.SetReferenceCalled);
        await store.DidNotReceiveWithAnyArgs().DeleteAsync(default);
    }

    [Fact]
    public async Task SetOwnIcon_NoStore_ThrowsNotSupported()
    {
        var identity = $"id-{Guid.NewGuid():N}";
        var sut = new IconTestUserService(null, new TestUser { Key = "u1", Identity = identity }, identity);
        await Assert.ThrowsAsync<NotSupportedException>(() => sut.SetOwnIconAsync([1], "image/png"));
    }

    [Fact]
    public async Task SetOwnIcon_NoCurrentUser_ThrowsUnauthorized()
    {
        var identity = $"id-{Guid.NewGuid():N}";
        var sut = new IconTestUserService(Substitute.For<IIconStore>(), null, identity);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetOwnIconAsync([1], "image/png"));
    }

    // ---- Admin set-for-user ----

    [Fact]
    public async Task SetUserIcon_ByKey_StoresAndDeletesPrevious()
    {
        var (sut, store) = Build(new TestUser { Key = "u1", EMail = "a@b.c", Icon = "old-ref" });
        store.SaveAsync(IconKind.User, "u1", Arg.Any<byte[]>(), "image/png").Returns("new-ref");

        await sut.SetUserIconAsync("u1", [1], "image/png");

        Assert.Equal("new-ref", sut.SetReference);
        await store.Received(1).DeleteAsync("old-ref");
    }

    [Fact]
    public async Task SetUserIcon_UnknownUser_Throws()
    {
        var (sut, _) = Build(new TestUser { Key = "u1", EMail = "a@b.c" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SetUserIconAsync("other", [1], "image/png"));
    }

    [Fact]
    public async Task ClearUserIcon_ByKey_ClearsAndDeletes()
    {
        var (sut, store) = Build(new TestUser { Key = "u1", EMail = "a@b.c", Icon = "ref-1" });

        await sut.ClearUserIconAsync("u1");

        Assert.Null(sut.SetReference);
        await store.Received(1).DeleteAsync("ref-1");
    }
}
