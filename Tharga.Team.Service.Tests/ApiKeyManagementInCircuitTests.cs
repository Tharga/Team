using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The API key services work in an interactive Blazor circuit, where there is no request to read the caller from (Tharga/Team#550).
/// </summary>
/// <remarks>
/// <b>These tests have no request at all.</b> <c>IHttpContextAccessor.HttpContext</c> is null, as it is for the circuit's calls, and the caller is only
/// what <see cref="ITeamPrincipalAccessor"/> gives: the circuit's current principal, which carries the member key for the selected team. A test that fakes
/// the request cannot catch this, which is how a personal key could be impossible to create from the page with every test green.
/// <para>
/// The refusals are the tests to keep: a caller only ever mints a key owned by themselves, and only an owner or an overseer manages a personal key.
/// </para>
/// </remarks>
public class ApiKeyManagementInCircuitTests
{
    private const string Team = "team-1";

    private static IApiKey Key(string id, string ownerMemberKey = null)
    {
        var k = Substitute.For<IApiKey>();
        k.Key.Returns(id);
        k.OwnerMemberKey.Returns(ownerMemberKey);
        return k;
    }

    private static async IAsyncEnumerable<IApiKey> Async(params IApiKey[] items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    private sealed class CircuitPrincipal(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }

    private static ClaimsPrincipal Circuit(string memberKey = null, bool overseer = false, string name = "alice")
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name), new(TeamClaimTypes.TeamKey, Team), new(TeamClaimTypes.AccessLevel, nameof(AccessLevel.Administrator)) };
        if (memberKey != null) claims.Add(new Claim(TeamClaimTypes.MemberKey, memberKey));
        if (overseer) claims.Add(new Claim(TeamClaimTypes.SystemScope, SystemApiKeyScopes.Oversee));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "circuit"));
    }

    /// <summary>A service whose request has no user, and whose caller is the circuit principal.</summary>
    private static (ApiKeyManagementService Sut, IApiKeyAdministrationService Inner) InACircuit(ClaimsPrincipal circuit, params IApiKey[] keys)
    {
        var inner = Substitute.For<IApiKeyAdministrationService>();
        inner.GetKeysAsync(Team).Returns(_ => Async(keys));

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext)null);

        return (new ApiKeyManagementService(inner, accessor, principalAccessor: new CircuitPrincipal(circuit)), inner);
    }

    private static async Task<string[]> Ids(IAsyncEnumerable<IApiKey> keys)
    {
        var list = new List<string>();
        await foreach (var k in keys) list.Add(k.Key);
        return [.. list];
    }

    // --- creating a personal key ---

    [Fact]
    public async Task APersonalKey_IsOwnedByTheCircuitCaller()
    {
        var (sut, inner) = InACircuit(Circuit(memberKey: "m1"));

        await sut.CreateKeyAsync(Team, "Mine", AccessLevel.User, ownerScoped: true);

        await inner.Received(1).CreateKeyAsync(Team, "Mine", AccessLevel.User, Arg.Any<string[]>(), Arg.Any<string[]>(), Arg.Any<DateTime?>(), Arg.Any<IReadOnlyList<Tag>>(), "alice", "m1");
    }

    /// <summary>Nobody can be named, so nobody owns it: a caller only ever mints a key owned by themselves.</summary>
    [Fact]
    public async Task APersonalKey_IsRefused_WhenTheCircuitCallerHasNoMemberKey()
    {
        var (sut, inner) = InACircuit(Circuit(memberKey: null));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateKeyAsync(Team, "Mine", AccessLevel.User, ownerScoped: true));

        Assert.Contains("no team-member context", error.Message);
        await inner.DidNotReceiveWithAnyArgs().CreateKeyAsync(default, default, default, default, default, default, default, default, default);
    }

    [Fact]
    public async Task APersonalKey_IsRefused_WhenThereIsNoCallerAtAll()
    {
        var inner = Substitute.For<IApiKeyAdministrationService>();
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext)null);
        var sut = new ApiKeyManagementService(inner, accessor, principalAccessor: new CircuitPrincipal(null));

        await Assert.ThrowsAnyAsync<Exception>(() => sut.CreateKeyAsync(Team, "Mine", AccessLevel.User, ownerScoped: true));

        await inner.DidNotReceiveWithAnyArgs().CreateKeyAsync(default, default, default, default, default, default, default, default, default);
    }

    /// <summary>A shared key from the same form never needed an owner, and still gets none.</summary>
    [Fact]
    public async Task ASharedKey_HasNoOwner_AndIsStampedWithTheCallersName()
    {
        var (sut, inner) = InACircuit(Circuit(memberKey: "m1"));

        await sut.CreateKeyAsync(Team, "Shared", AccessLevel.User);

        await inner.Received(1).CreateKeyAsync(Team, "Shared", AccessLevel.User, Arg.Any<string[]>(), Arg.Any<string[]>(), Arg.Any<DateTime?>(), Arg.Any<IReadOnlyList<Tag>>(), "alice", null);
    }

    // --- seeing and managing them ---

    [Fact]
    public async Task TheCircuitCaller_SeesTheirOwnPersonalKeys_AndNotSomebodyElses()
    {
        var (sut, _) = InACircuit(Circuit(memberKey: "m1"), Key("team-wide"), Key("mine", "m1"), Key("theirs", "m2"));

        var ids = await Ids(sut.GetKeysAsync(Team, PrivateKeyScope.Mine));

        Assert.Equal(["team-wide", "mine"], ids);
    }

    [Fact]
    public async Task AnOverseer_InACircuit_SeesEveryPersonalKey()
    {
        var (sut, _) = InACircuit(Circuit(memberKey: "m1", overseer: true), Key("mine", "m1"), Key("theirs", "m2"));

        var ids = await Ids(sut.GetKeysAsync(Team, PrivateKeyScope.All));

        Assert.Equal(["mine", "theirs"], ids);
    }

    [Fact]
    public async Task TheCircuitCaller_CanDeleteTheirOwnPersonalKey()
    {
        var (sut, inner) = InACircuit(Circuit(memberKey: "m1"), Key("mine", "m1"));

        await sut.DeleteKeyAsync(Team, "mine");

        await inner.Received(1).DeleteKeyAsync(Team, "mine");
    }

    [Fact]
    public async Task TheCircuitCaller_CannotDeleteSomebodyElsesPersonalKey()
    {
        var (sut, inner) = InACircuit(Circuit(memberKey: "m1"), Key("theirs", "m2"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.DeleteKeyAsync(Team, "theirs"));

        await inner.DidNotReceiveWithAnyArgs().DeleteKeyAsync(default, default);
    }

    [Fact]
    public async Task AnOverseer_InACircuit_CanDeleteSomebodyElsesPersonalKey()
    {
        var (sut, inner) = InACircuit(Circuit(memberKey: "m1", overseer: true), Key("theirs", "m2"));

        await sut.DeleteKeyAsync(Team, "theirs");

        await inner.Received(1).DeleteKeyAsync(Team, "theirs");
    }

    // --- the request still counts where there is no circuit ---

    /// <summary>Without a principal accessor, the request is still the caller, as it is for the API.</summary>
    [Fact]
    public async Task WithNoPrincipalAccessor_TheRequestIsStillTheCaller()
    {
        var inner = Substitute.For<IApiKeyAdministrationService>();
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext { User = Circuit(memberKey: "m9") });
        var sut = new ApiKeyManagementService(inner, accessor);

        await sut.CreateKeyAsync(Team, "Mine", AccessLevel.User, ownerScoped: true);

        await inner.Received(1).CreateKeyAsync(Team, "Mine", AccessLevel.User, Arg.Any<string[]>(), Arg.Any<string[]>(), Arg.Any<DateTime?>(), Arg.Any<IReadOnlyList<Tag>>(), "alice", "m9");
    }
}
