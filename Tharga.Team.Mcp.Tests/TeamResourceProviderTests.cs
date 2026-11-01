using System.Text.Json;
using Tharga.Mcp;
using Tharga.Team.Mcp;
using Tharga.Team;

namespace Tharga.Team.Mcp.Tests;

public class TeamResourceProviderTests
{
    private readonly ITeamManagementService _teamService = Substitute.For<ITeamManagementService>();
    private readonly IApiKeyManagementService _apiKeyService = Substitute.For<IApiKeyManagementService>();
    private readonly IMcpScopeChecker _scope = Substitute.For<IMcpScopeChecker>();

    private TeamMcpContext MakeContext(string teamId)
        => TestMcpContextFactory.Create(teamId: teamId, scope: McpScope.Team);

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(params T[] items)
    {
        foreach (var item in items) yield return item;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ListResourcesAsync_NoTeamId_ReturnsEmpty()
    {
        var sut = new TeamResourceProvider(_teamService, _apiKeyService);

        var result = await sut.ListResourcesAsync(MakeContext(teamId: null), TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListResourcesAsync_WithTeamId_Returns_Three_When_ApiKeyServiceRegistered_AndTheCallerMayManageKeys()
    {
        _scope.Has(ApiKeyScopes.Manage).Returns(true);
        var sut = new TeamResourceProvider(_teamService, _apiKeyService, _scope);

        var result = await sut.ListResourcesAsync(MakeContext("T-1"), TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, r => r.Uri == TeamResourceProvider.TeamUri);
        Assert.Contains(result, r => r.Uri == TeamResourceProvider.MembersUri);
        Assert.Contains(result, r => r.Uri == TeamResourceProvider.ApiKeysUri);
    }

    [Fact]
    public async Task ListResourcesAsync_WithTeamId_OmitsApiKeys_When_ApiKeyServiceNotRegistered()
    {
        _scope.Has(ApiKeyScopes.Manage).Returns(true);
        var sut = new TeamResourceProvider(_teamService, apiKeyManagementService: null, scopeChecker: _scope);

        var result = await sut.ListResourcesAsync(MakeContext("T-1"), TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, r => r.Uri == TeamResourceProvider.ApiKeysUri);
    }

    /// <summary>Listing asks the question reading asks: a resource the caller would be refused is not advertised.</summary>
    [Fact]
    public async Task ListResourcesAsync_OmitsApiKeys_WhenTheCallerMayNotManageKeys()
    {
        _scope.Has(ApiKeyScopes.Manage).Returns(false);
        var sut = new TeamResourceProvider(_teamService, _apiKeyService, _scope);

        var result = await sut.ListResourcesAsync(MakeContext("T-1"), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(result, r => r.Uri == TeamResourceProvider.ApiKeysUri);
        Assert.Equal(2, result.Count);
    }

    /// <summary>With no scope checker there is no way to know the caller may, so nothing is advertised: it fails closed.</summary>
    [Fact]
    public async Task ListResourcesAsync_OmitsApiKeys_WhenThereIsNoScopeCheckerToAsk()
    {
        var sut = new TeamResourceProvider(_teamService, _apiKeyService);

        var result = await sut.ListResourcesAsync(MakeContext("T-1"), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(result, r => r.Uri == TeamResourceProvider.ApiKeysUri);
    }

    [Fact]
    public async Task ReadResourceAsync_NoTeamId_Throws()
    {
        var sut = new TeamResourceProvider(_teamService, _apiKeyService);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.ReadResourceAsync(TeamResourceProvider.TeamUri, MakeContext(teamId: null), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadResourceAsync_UnknownUri_Throws()
    {
        var sut = new TeamResourceProvider(_teamService, _apiKeyService);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ReadResourceAsync("team://team/bogus", MakeContext("T-1"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadResourceAsync_TeamUri_ReturnsTeamMetadata()
    {
        var team = Substitute.For<ITeam>();
        team.Key.Returns("T-1");
        team.Name.Returns("Acme");
        team.Icon.Returns((string)null);
        team.ConsentedRoles.Returns(new[] { "viewer" });
        _teamService.GetTeamByKeyAsync("T-1").Returns(team);

        var sut = new TeamResourceProvider(_teamService, _apiKeyService);
        var content = await sut.ReadResourceAsync(TeamResourceProvider.TeamUri, MakeContext("T-1"), TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(content.Text);
        var root = doc.RootElement;
        Assert.Equal("T-1", root.GetProperty("key").GetString());
        Assert.Equal("Acme", root.GetProperty("name").GetString());
        Assert.Equal(1, root.GetProperty("consentedRoles").GetArrayLength());
    }

    /// <summary>
    /// A caller authenticated by API key has no *user*, so scanning the caller's memberships found nothing
    /// and every read failed — even for the team the key names. Listing advertised the resource and
    /// reading refused it (Tharga/Mcp probing, 2026-07-31).
    /// </summary>
    [Fact]
    public async Task ReadResourceAsync_TeamUri_SucceedsForACallerWithNoUserMemberships()
    {
        var team = Substitute.For<ITeam>();
        team.Key.Returns("T-1");
        team.Name.Returns("Acme");
        team.ConsentedRoles.Returns(Array.Empty<string>());
        _teamService.GetTeamByKeyAsync("T-1").Returns(team);

        var sut = new TeamResourceProvider(_teamService, _apiKeyService);
        var content = await sut.ReadResourceAsync(TeamResourceProvider.TeamUri, MakeContext("T-1"), TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(content.Text);
        Assert.Equal("T-1", doc.RootElement.GetProperty("key").GetString());
    }

    /// <summary>
    /// The safety property the fix rests on. `GetTeamByKeyAsync` is deliberately unauthorized, so reading
    /// by key is only safe while the key comes from the caller's own server-issued `TeamKey` claim and
    /// never from the request. This pins that: the team read is always the one the context names.
    /// </summary>
    [Fact]
    public async Task ReadResourceAsync_TeamUri_ReadsOnlyTheTeamTheContextNames()
    {
        var own = Substitute.For<ITeam>();
        own.Key.Returns("T-1");
        own.ConsentedRoles.Returns(Array.Empty<string>());
        _teamService.GetTeamByKeyAsync("T-1").Returns(own);

        var sut = new TeamResourceProvider(_teamService, _apiKeyService);
        await sut.ReadResourceAsync(TeamResourceProvider.TeamUri, MakeContext("T-1"), TestContext.Current.CancellationToken);

        await _teamService.Received(1).GetTeamByKeyAsync("T-1");
        await _teamService.DidNotReceive().GetTeamByKeyAsync(Arg.Is<string>(x => x != "T-1"));
    }

    [Fact]
    public async Task ReadResourceAsync_TeamUri_UnknownTeam_Throws()
    {
        _teamService.GetTeamByKeyAsync("T-9").Returns((ITeam)null);

        var sut = new TeamResourceProvider(_teamService, _apiKeyService);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ReadResourceAsync(TeamResourceProvider.TeamUri, MakeContext("T-9"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadResourceAsync_MembersUri_ReturnsMembers()
    {
        var m1 = Substitute.For<ITeamMember>();
        m1.Key.Returns("u-1");
        m1.Name.Returns("One");
        m1.AccessLevel.Returns(AccessLevel.Owner);
        m1.State.Returns(MembershipState.Member);
        var m2 = Substitute.For<ITeamMember>();
        m2.Key.Returns("u-2");
        m2.AccessLevel.Returns(AccessLevel.User);
        m2.Invitation.Returns(new Invitation { InviteKey = "inv-abc", EMail = "two@example.com", InviteTime = DateTime.UtcNow });
        _teamService.GetMembersAsync("T-1").Returns(ToAsyncEnumerable(m1, m2));

        var sut = new TeamResourceProvider(_teamService, _apiKeyService);
        var content = await sut.ReadResourceAsync(TeamResourceProvider.MembersUri, MakeContext("T-1"), TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(content.Text);
        var root = doc.RootElement;
        Assert.Equal("T-1", root.GetProperty("teamKey").GetString());
        var items = root.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.False(items[0].GetProperty("invited").GetBoolean());
        Assert.True(items[1].GetProperty("invited").GetBoolean());
    }

    [Fact]
    public async Task ReadResourceAsync_ApiKeysUri_ReturnsRedactedKeys()
    {
        var key = Substitute.For<IApiKey>();
        key.Key.Returns("k-1");
        key.Name.Returns("Default");
        key.ApiKey.Returns("RAW-SECRET-VALUE-SHOULD-BE-REDACTED");
        key.AccessLevel.Returns(AccessLevel.Administrator);
        key.Roles.Returns(new[] { "Editor" });
        key.CreatedBy.Returns("alice");
        _apiKeyService.GetKeysAsync("T-1", PrivateKeyScope.All, false).Returns(ToAsyncEnumerable(key));

        var sut = new TeamResourceProvider(_teamService, _apiKeyService);
        var content = await sut.ReadResourceAsync(TeamResourceProvider.ApiKeysUri, MakeContext("T-1"), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("RAW-SECRET-VALUE", content.Text);

        using var doc = JsonDocument.Parse(content.Text);
        var item = doc.RootElement.GetProperty("items")[0];
        Assert.Equal("k-1", item.GetProperty("key").GetString());
        Assert.Equal("Default", item.GetProperty("name").GetString());
        Assert.Equal("alice", item.GetProperty("createdBy").GetString());
        Assert.False(item.TryGetProperty("apiKey", out _));
    }

    [Fact]
    public async Task ReadResourceAsync_ApiKeysUri_NoApiKeyService_Throws()
    {
        var sut = new TeamResourceProvider(_teamService, apiKeyManagementService: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ReadResourceAsync(TeamResourceProvider.ApiKeysUri, MakeContext("T-1"), TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The listing goes through the scope-checked service, which requires <c>apikey:manage</c> and filters private keys, so the
    /// resource shows what <c>GetKeysAsync</c> shows that caller and nothing the repository could: the caller's own private
    /// keys, every private key only for an <c>apikeys:oversee</c> holder, and never because the host allowed privilege.
    /// </summary>
    [Fact]
    public async Task ReadResourceAsync_ApiKeysUri_ReadsThroughTheCheckedService_WithTheSameRulesAsGetKeysAsync()
    {
        _apiKeyService.GetKeysAsync(Arg.Any<string>(), Arg.Any<PrivateKeyScope>(), Arg.Any<bool>()).Returns(ToAsyncEnumerable<IApiKey>());

        var sut = new TeamResourceProvider(_teamService, _apiKeyService);
        await sut.ReadResourceAsync(TeamResourceProvider.ApiKeysUri, MakeContext("T-1"), TestContext.Current.CancellationToken);

        _apiKeyService.Received(1).GetKeysAsync("T-1", PrivateKeyScope.All, false);
        _apiKeyService.ReceivedWithAnyArgs(1).GetKeysAsync(default, default, default);
    }

    /// <summary>The service refuses a caller without <c>apikey:manage</c>; the resource never turns that into a listing.</summary>
    [Fact]
    public async Task ReadResourceAsync_ApiKeysUri_IsRefused_WhenTheServiceRefuses()
    {
        _apiKeyService.GetKeysAsync("T-1", PrivateKeyScope.All, false).Returns(Refused());

        var sut = new TeamResourceProvider(_teamService, _apiKeyService);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.ReadResourceAsync(TeamResourceProvider.ApiKeysUri, MakeContext("T-1"), TestContext.Current.CancellationToken));
    }

    /// <summary>Another member's private key is the thing the service holds back, so it must not be in what the resource returns.</summary>
    [Fact]
    public async Task ReadResourceAsync_ApiKeysUri_ShowsOnlyWhatTheServiceReturnsTheCaller()
    {
        var shared = Substitute.For<IApiKey>();
        shared.Key.Returns("shared");
        _apiKeyService.GetKeysAsync("T-1", PrivateKeyScope.All, false).Returns(ToAsyncEnumerable(shared));

        var sut = new TeamResourceProvider(_teamService, _apiKeyService);
        var content = await sut.ReadResourceAsync(TeamResourceProvider.ApiKeysUri, MakeContext("T-1"), TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(content.Text);
        Assert.Equal(["shared"], doc.RootElement.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("key").GetString()));
    }

    private static async IAsyncEnumerable<IApiKey> Refused()
    {
        await Task.Yield();
        throw new UnauthorizedAccessException("Missing required scope 'apikey:manage'.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
