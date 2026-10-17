using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Tharga.Team;

namespace Tharga.Team.Service.Tests;

public class ApiKeyManagementOwnerScopeTests
{
    private const string Team = "team-1";

    private static IApiKey Key(string id, string ownerMemberKey = null)
    {
        var k = Substitute.For<IApiKey>();
        k.Key.Returns(id);
        k.OwnerMemberKey.Returns(ownerMemberKey);
        return k;
    }

    private static async IAsyncEnumerable<IApiKey> ToAsyncEnumerable(params IApiKey[] items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    // Builds a management service whose principal has the given member key / access level / oversight scope.
    private static (ApiKeyManagementService sut, IApiKeyAdministrationService inner) Build(
        string memberKey = null, string accessLevel = null, bool overseer = false, params IApiKey[] keys)
    {
        var inner = Substitute.For<IApiKeyAdministrationService>();
        inner.GetKeysAsync(Team).Returns(_ => ToAsyncEnumerable(keys));

        var claims = new List<Claim>();
        if (memberKey != null) claims.Add(new Claim(TeamClaimTypes.MemberKey, memberKey));
        if (accessLevel != null) claims.Add(new Claim(TeamClaimTypes.AccessLevel, accessLevel));
        if (overseer) claims.Add(new Claim(TeamClaimTypes.SystemScope, SystemApiKeyScopes.Oversee));

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test", ClaimTypes.Name, ClaimTypes.Role)),
        });

        return (new ApiKeyManagementService(inner, accessor), inner);
    }

    private static async Task<string[]> KeyIds(IAsyncEnumerable<IApiKey> keys)
    {
        var list = new List<string>();
        await foreach (var k in keys) list.Add(k.Key);
        return list.ToArray();
    }

    [Fact]
    public async Task None_Excludes_All_Private_Keys()
    {
        var (sut, _) = Build(memberKey: "m1", keys: [Key("team-wide"), Key("priv", "m1")]);

        var ids = await KeyIds(sut.GetKeysAsync(Team)); // default PrivateKeyScope.None

        Assert.Equal(["team-wide"], ids);
    }

    [Fact]
    public async Task Mine_Includes_Only_Callers_Own_Private_Keys()
    {
        var (sut, _) = Build(memberKey: "m1", keys: [Key("team-wide"), Key("mine", "m1"), Key("theirs", "m2")]);

        var ids = await KeyIds(sut.GetKeysAsync(Team, PrivateKeyScope.Mine));

        Assert.Equal(["team-wide", "mine"], ids);
    }

    [Fact]
    public async Task All_Without_Privilege_Hides_Other_Members_Private_Keys()
    {
        var (sut, _) = Build(memberKey: "m1", accessLevel: "Administrator", keys: [Key("mine", "m1"), Key("theirs", "m2")]);

        // allowPrivileged defaults false → Administrator does NOT see others' private keys
        var ids = await KeyIds(sut.GetKeysAsync(Team, PrivateKeyScope.All));

        Assert.Equal(["mine"], ids);
    }

    [Fact]
    public async Task All_With_Privilege_Lets_Administrator_See_Others_Private_Keys()
    {
        var (sut, _) = Build(memberKey: "m1", accessLevel: "Administrator", keys: [Key("mine", "m1"), Key("theirs", "m2")]);

        var ids = await KeyIds(sut.GetKeysAsync(Team, PrivateKeyScope.All, allowPrivileged: true));

        Assert.Equal(["mine", "theirs"], ids);
    }

    [Fact]
    public async Task AllowPrivileged_Does_Not_Escalate_NonPrivileged_Caller()
    {
        var (sut, _) = Build(memberKey: "m1", accessLevel: "User", keys: [Key("theirs", "m2")]);

        // allowPrivileged true but caller is only User → no privileged visibility
        var ids = await KeyIds(sut.GetKeysAsync(Team, PrivateKeyScope.All, allowPrivileged: true));

        Assert.Empty(ids);
    }

    [Fact]
    public async Task Overseer_Sees_All_Private_Keys()
    {
        var (sut, _) = Build(memberKey: "m1", overseer: true, keys: [Key("mine", "m1"), Key("theirs", "m2")]);

        var ids = await KeyIds(sut.GetKeysAsync(Team, PrivateKeyScope.All));

        Assert.Equal(["mine", "theirs"], ids);
    }

    [Fact]
    public async Task Mutation_Rejected_For_NonOwner_NonOverseer()
    {
        var (sut, inner) = Build(memberKey: "m1", accessLevel: "Administrator", keys: [Key("theirs", "m2")]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.RefreshKeyAsync(Team, "theirs"));
        await inner.DidNotReceive().RefreshKeyAsync(Team, "theirs");
    }

    [Fact]
    public async Task Mutation_Allowed_For_Owner()
    {
        var (sut, inner) = Build(memberKey: "m1", keys: [Key("mine", "m1")]);

        await sut.DeleteKeyAsync(Team, "mine");

        await inner.Received(1).DeleteKeyAsync(Team, "mine");
    }

    [Fact]
    public async Task Mutation_Allowed_For_Overseer()
    {
        var (sut, inner) = Build(memberKey: "m1", overseer: true, keys: [Key("theirs", "m2")]);

        await sut.LockKeyAsync(Team, "theirs");

        await inner.Received(1).LockKeyAsync(Team, "theirs");
    }

    [Fact]
    public async Task Mutation_PassesThrough_For_TeamWide_Keys()
    {
        var (sut, inner) = Build(memberKey: "m1", keys: [Key("team-wide")]);

        await sut.RefreshKeyAsync(Team, "team-wide");

        await inner.Received(1).RefreshKeyAsync(Team, "team-wide");
    }

    [Fact]
    public async Task OwnerScoped_Mint_Forces_Callers_MemberKey()
    {
        var (sut, inner) = Build(memberKey: "m1");

        await sut.CreateKeyAsync(Team, "My Key", AccessLevel.User, ownerScoped: true);

        await inner.Received(1).CreateKeyAsync(Team, "My Key", AccessLevel.User, null, null, null, null, Arg.Any<string>(), "m1");
    }

    [Fact]
    public async Task OwnerScoped_Mint_Throws_Without_Member_Context()
    {
        var (sut, _) = Build(); // no MemberKey claim

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.CreateKeyAsync(Team, "My Key", AccessLevel.User, ownerScoped: true));
    }
}
