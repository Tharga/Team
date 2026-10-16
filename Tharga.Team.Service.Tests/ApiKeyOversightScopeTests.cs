using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Oversight of other members' private keys is the <c>apikeys:oversee</c> system scope, which a host grants
/// to whichever roles should have it (Tharga/Team#323). The <c>Developer</c> role keeps working until 4.0, and
/// the scope only counts as a <i>system</i> grant, never as a team scope of the same name.
/// </summary>
public class ApiKeyOversightScopeTests
{
    private const string Team = "team-1";

    private static IApiKey Key(string id, string owner = null)
    {
        var key = Substitute.For<IApiKey>();
        key.Key.Returns(id);
        key.OwnerMemberKey.Returns(owner);
        return key;
    }

    private static async IAsyncEnumerable<IApiKey> Keys(params IApiKey[] items)
    {
        foreach (var item in items) yield return item;
        await Task.CompletedTask;
    }

    private static (ApiKeyManagementService Sut, IApiKeyAdministrationService Inner) Build(params Claim[] claims)
    {
        var inner = Substitute.For<IApiKeyAdministrationService>();
        inner.GetKeysAsync(Team).Returns(_ => Keys(Key("team-wide"), Key("someone-elses", "m2")));

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(TeamClaimTypes.MemberKey, "m1"), .. claims], "test", ClaimTypes.Name, ClaimTypes.Role))
        });
        return (new ApiKeyManagementService(inner, accessor), inner);
    }

    private static readonly Claim OverseeSystemScope = new(TeamClaimTypes.SystemScope, SystemApiKeyScopes.Oversee);
    private static readonly Claim OverseeTeamScope = new(TeamClaimTypes.Scope, SystemApiKeyScopes.Oversee);
    private static readonly Claim DeveloperRole = new(ClaimTypes.Role, "Developer");
    private static readonly Claim AdministratorRole = new(ClaimTypes.Role, "Administrator");

    private static async Task<string[]> Visible(ApiKeyManagementService sut)
        => await sut.GetKeysAsync(Team, PrivateKeyScope.All).Select(x => x.Key).ToArrayAsync();

    [Fact]
    public async Task OverseeSystemScope_SeesOtherMembersPrivateKeys()
    {
        var (sut, _) = Build(OverseeSystemScope, AdministratorRole);
        Assert.Equal(["team-wide", "someone-elses"], await Visible(sut));
    }

    [Fact]
    public async Task OverseeSystemScope_ManagesOtherMembersPrivateKeys()
    {
        var (sut, inner) = Build(OverseeSystemScope);

        await sut.DeleteKeyAsync(Team, "someone-elses");

        await inner.Received(1).DeleteKeyAsync(Team, "someone-elses");
    }

    [Fact]
    public async Task DeveloperRole_StillOversees_Until4()
    {
        var (sut, inner) = Build(DeveloperRole);

        Assert.Equal(["team-wide", "someone-elses"], await Visible(sut));
        await sut.DeleteKeyAsync(Team, "someone-elses");
        await inner.Received(1).DeleteKeyAsync(Team, "someone-elses");
    }

    [Fact]
    public async Task SameNameAsATeamScope_DoesNotOversee()
    {
        var (sut, inner) = Build(OverseeTeamScope);

        Assert.Equal(["team-wide"], await Visible(sut));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.DeleteKeyAsync(Team, "someone-elses"));
        await inner.DidNotReceiveWithAnyArgs().DeleteKeyAsync(default, default);
    }

    [Fact]
    public async Task AnotherSystemRoleWithoutTheScope_DoesNotOversee()
    {
        var (sut, _) = Build(AdministratorRole);
        Assert.Equal(["team-wide"], await Visible(sut));
    }
}
