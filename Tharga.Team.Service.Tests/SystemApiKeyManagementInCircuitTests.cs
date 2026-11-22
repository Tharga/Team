using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Personal system keys work in an interactive Blazor circuit too, where there is no request to read the caller from (Tharga/Team#550).
/// </summary>
/// <remarks>
/// Same shape as <see cref="ApiKeyManagementInCircuitTests"/>: <c>IHttpContextAccessor.HttpContext</c> is null and the caller is only what
/// <see cref="ITeamPrincipalAccessor"/> gives. An API key is still not a person, and a caller who cannot be named still owns nothing.
/// </remarks>
public class SystemApiKeyManagementInCircuitTests
{
    private const string Read = "support:all:read";

    private sealed class CircuitPrincipal(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }

    private static IApiKey Key(string id, string owner = null)
    {
        var k = Substitute.For<IApiKey>();
        k.Key.Returns(id);
        k.OwnerMemberKey.Returns(owner);
        k.TeamKey.Returns((string)null);
        k.SystemScopes.Returns([Read]);
        return k;
    }

    private static async IAsyncEnumerable<IApiKey> Async(params IApiKey[] items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    private static ClaimsPrincipal Circuit(string memberKey = null, bool apiKey = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "alice"), new(TeamClaimTypes.SystemScope, Read) };
        if (memberKey != null) claims.Add(new Claim(TeamClaimTypes.MemberKey, memberKey));
        if (apiKey) claims.Add(new Claim(TeamClaimTypes.ApiKeyId, "key-1"));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "circuit"));
    }

    private static (SystemApiKeyManagementService Sut, IApiKeyAdministrationService Inner) InACircuit(ClaimsPrincipal circuit, params IApiKey[] keys)
    {
        var inner = Substitute.For<IApiKeyAdministrationService>();
        inner.GetSystemKeysAsync().Returns(_ => Async(keys));

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext)null);

        return (new SystemApiKeyManagementService(inner, accessor, principalAccessor: new CircuitPrincipal(circuit)), inner);
    }

    [Fact]
    public async Task APersonalSystemKey_IsOwnedByTheCircuitCaller()
    {
        var (sut, inner) = InACircuit(Circuit(memberKey: "u1"));

        await sut.CreateSystemKeyAsync("Mine", [Read], personal: true);

        await inner.Received(1).CreatePersonalSystemKeyAsync("Mine", Arg.Any<string[]>(), "u1", Arg.Any<DateTime?>(), "alice");
    }

    [Fact]
    public async Task APersonalSystemKey_IsRefused_WhenTheCircuitCallerCannotBeNamed()
    {
        var (sut, inner) = InACircuit(Circuit(memberKey: null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateSystemKeyAsync("Mine", [Read], personal: true));

        await inner.DidNotReceiveWithAnyArgs().CreatePersonalSystemKeyAsync(default, default, default, default, default);
    }

    /// <summary>An API key is not a person, in a circuit as anywhere: it cannot own a personal key.</summary>
    [Fact]
    public async Task AnApiKeyCaller_StillOwnsNothing()
    {
        var (sut, inner) = InACircuit(Circuit(memberKey: "u1", apiKey: true));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateSystemKeyAsync("Mine", [Read], personal: true));

        await inner.DidNotReceiveWithAnyArgs().CreatePersonalSystemKeyAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task TheCircuitCaller_SeesTheirOwnPersonalSystemKeys_AndNotSomebodyElses()
    {
        var (sut, _) = InACircuit(Circuit(memberKey: "u1"), Key("shared"), Key("mine", "u1"), Key("theirs", "u2"));

        var ids = new List<string>();
        await foreach (var k in sut.GetSystemKeysAsync(PrivateKeyScope.Mine)) ids.Add(k.Key);

        Assert.Equal(["shared", "mine"], ids);
    }

    [Fact]
    public async Task TheCircuitCaller_CannotDeleteSomebodyElsesPersonalSystemKey()
    {
        var (sut, inner) = InACircuit(Circuit(memberKey: "u1"), Key("theirs", "u2"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.DeleteSystemKeyAsync("theirs"));

        await inner.DidNotReceiveWithAnyArgs().DeleteSystemKeyAsync(default);
    }
}
