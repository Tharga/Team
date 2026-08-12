namespace Tharga.Team.Service.Tests;

/// <summary>
/// The custom-roles cache on <c>TeamServiceBase</c>: the claims path reads a team's custom roles on every
/// authenticating request when dynamic tenant roles are enabled, and that read went to the store each time.
/// </summary>
/// <remarks>
/// <b>Each test gets its own <see cref="InMemoryTeamCache"/>.</b> That is what injecting
/// <see cref="ITeamCache"/> buys here beyond the multi-instance case it was added for: a service constructed
/// without one falls back to a process-wide shared instance, and an assertion about store reads then depends
/// on which test primed it first — which has already produced one false failure (see
/// <see cref="MemberSuspensionTests.Suspending_DropsTheCachedMember"/>). Keys are still distinct per test so
/// the intent survives someone removing the injection.
/// <para>
/// These tests count <c>GetTeamCallCount</c> rather than asserting on returned roles, because the point is
/// which reads reach the store. Correctness of the merge itself is <see cref="TenantRoleServiceTests"/>.
/// </para>
/// <para>
/// <b>Not covered here: the invalidation in <c>CreateTeamAsync</c>.</b> It guards a host that deleted a team
/// around <c>DeleteTeamAsync</c>, leaving the entry behind for the key to be handed out again — and it
/// cannot be reached from a test, because the public create path generates its own key and offers no way to
/// ask for one. <see cref="DeletingTheTeam_DropsTheEntry"/> covers the path that actually frees a key.
/// </para>
/// </remarks>
public class TeamCustomRolesCacheTests
{
    private static readonly IReadOnlyList<TenantRoleDefinition> Registrar =
        [new TenantRoleDefinition("Registrar", ["case:read"])];

    private static TestTeamService Build(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles = null)
    {
        var caller = Substitute.For<IUser>();
        caller.Key.Returns("cache-caller");

        var userService = Substitute.For<IUserService>();
        userService.GetCurrentUserAsync().Returns(caller);
        userService.GetCurrentUserAsync(Arg.Any<System.Security.Claims.ClaimsPrincipal>()).Returns(caller);

        var sut = new TestTeamService(userService, new InMemoryTeamCache());
        sut.AddTeam(teamKey, "Cache Probe");
        if (customRoles != null) sut.SeedCustomRoles(teamKey, customRoles);
        return sut;
    }

    [Fact]
    public async Task RepeatedRead_ReachesTheStoreOnce()
    {
        const string teamKey = "roles-cache-repeat";
        var sut = Build(teamKey, Registrar);

        var first = await sut.GetTeamCustomRolesAsync(teamKey);
        var second = await sut.GetTeamCustomRolesAsync(teamKey);

        Assert.Equal(1, sut.GetTeamCallCount);
        Assert.Equal("Registrar", Assert.Single(first).Name);
        Assert.Same(first, second);
    }

    /// <summary>
    /// An empty answer must cache too. Caching only non-empty results would leave the common case — a team
    /// with no custom roles at all — reading the store on every request, which is the whole defect.
    /// </summary>
    [Fact]
    public async Task ATeamWithNoCustomRoles_IsAlsoCached()
    {
        const string teamKey = "roles-cache-empty";
        var sut = Build(teamKey);

        Assert.Empty(await sut.GetTeamCustomRolesAsync(teamKey));
        Assert.Empty(await sut.GetTeamCustomRolesAsync(teamKey));

        Assert.Equal(1, sut.GetTeamCallCount);
    }

    [Fact]
    public async Task Writing_DropsTheEntry()
    {
        const string teamKey = "roles-cache-write";
        var sut = Build(teamKey);

        await sut.GetTeamCustomRolesAsync(teamKey);
        await sut.SetTeamCustomRolesAsync(teamKey, Registrar);
        var after = await sut.GetTeamCustomRolesAsync(teamKey);

        Assert.Equal(2, sut.GetTeamCallCount);
        Assert.Equal("Registrar", Assert.Single(after).Name);
    }

    /// <summary>
    /// A deleted team's key is handed out again — <c>GetRandomUnsusedTeamKey</c> only checks that no team
    /// currently holds it — so a surviving entry would give a brand-new team the deleted one's roles.
    /// </summary>
    [Fact]
    public async Task DeletingTheTeam_DropsTheEntry()
    {
        const string teamKey = "roles-cache-delete";
        var sut = Build(teamKey, Registrar);

        Assert.Single(await sut.GetTeamCustomRolesAsync(teamKey));

        await sut.DeleteTeamAsync<TestMember>(teamKey);
        var after = await sut.GetTeamCustomRolesAsync(teamKey);

        Assert.Empty(after);

        // Three, not two, since soft delete: deleting now reads the team's roster first so the members can
        // be evicted from the cache — without which a cached membership keeps authorizing a deleted team.
        // The extra read is the intended cost of that fix and happens once per delete.
        Assert.Equal(3, sut.GetTeamCallCount);
    }

    /// <summary>
    /// A null key must not reach the dictionary — <c>ConcurrentDictionary</c> throws on one — and must
    /// behave exactly as it did before the cache existed.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AnUnusableKey_BypassesTheCache(string teamKey)
    {
        var sut = Build("roles-cache-unusable");

        Assert.Empty(await sut.GetTeamCustomRolesAsync(teamKey));
        Assert.Empty(await sut.GetTeamCustomRolesAsync(teamKey));

        // Passed through both times rather than answered from an entry stored under the unusable key.
        Assert.Equal(2, sut.GetTeamCallCount);
    }

    /// <summary>
    /// The reason the cache exists: this is the exact call the claims transformation makes on every
    /// authenticating request once <c>AddThargaDynamicTenantRoles</c> is registered.
    /// </summary>
    [Fact]
    public async Task TheClaimsPathRead_ReachesTheStoreOnce()
    {
        const string teamKey = "roles-cache-claims";
        var sut = Build(teamKey, Registrar);

        var scopes = new ScopeRegistry();
        var tenantRoles = new TenantRoleService(sut, scopes);

        var first = await tenantRoles.GetEffectiveScopesAsync(teamKey, AccessLevel.Viewer, ["Registrar"], []);
        await tenantRoles.GetEffectiveScopesAsync(teamKey, AccessLevel.Viewer, ["Registrar"], []);
        await tenantRoles.GetEffectiveScopesAsync(teamKey, AccessLevel.Viewer, ["Registrar"], []);

        Assert.Contains("case:read", first);
        Assert.Equal(1, sut.GetTeamCallCount);
    }
}
