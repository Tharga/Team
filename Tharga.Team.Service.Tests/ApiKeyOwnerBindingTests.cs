using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// A private API key is its owner's credential, so it cannot outlive or outrank them (Tharga/Team#323).
/// </summary>
/// <remarks>
/// The key is refused once its owner can no longer act in the team (gone, not accepted, suspended, disabled,
/// deleted), works again when that is lifted, never carries a scope the owner does not hold now, and refuses
/// rather than guesses when the owner cannot be looked up. A team-wide key is untouched by all of it.
/// </remarks>
public class ApiKeyOwnerBindingTests
{
    private const string TeamKey = "acme";
    private const string OwnerKey = "user-1";
    private const string Secret = "the-secret";
    private const string Read = "data:read";
    private const string Write = "data:write";

    private readonly IApiKeyAdministrationService _apiKeyService = Substitute.For<IApiKeyAdministrationService>();
    private readonly ITeamService _teamService = Substitute.For<ITeamService>();
    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly ScopeRegistry _scopes = new();

    public ApiKeyOwnerBindingTests()
    {
        _scopes.Register(Read, AccessLevel.Viewer);
        _scopes.Register(Write, AccessLevel.User);

        var team = Substitute.For<ITeam>();
        team.Key.Returns(TeamKey);
        _teamService.GetTeamByKeyAsync(TeamKey).Returns(team);
    }

    private sealed record Member : ITeamMember
    {
        public string Key { get; init; } = OwnerKey;
        public string Name { get; init; }
        public Invitation Invitation { get; init; }
        public DateTime? LastSeen { get; init; }
        public MembershipState? State { get; init; } = MembershipState.Member;
        public AccessLevel? AccessLevel { get; init; } = Tharga.Team.AccessLevel.User;
        public string[] TenantRoles { get; init; } = [];
        public string[] ScopeOverrides { get; init; } = [];
        public DateTime? SuspendedAt { get; init; }
    }

    private sealed record User : IUser
    {
        public string Key { get; init; } = OwnerKey;
        public string Identity { get; init; } = "sub-1";
        public string Name { get; init; }
        public string EMail { get; init; }
        public DateTime? DisabledAt { get; init; }
    }

    private void GivenKey(AccessLevel level = AccessLevel.User, string owner = OwnerKey)
    {
        var key = Substitute.For<IApiKey>();
        key.Key.Returns("key-id");
        key.TeamKey.Returns(TeamKey);
        key.Name.Returns("Personal key");
        key.Tags.Returns(Array.Empty<Tag>());
        key.AccessLevel.Returns(level);
        key.OwnerMemberKey.Returns(owner);
        _apiKeyService.GetByApiKeyAsync(Secret).Returns(key);
    }

    private void GivenOwner(Member member = null, User user = null)
    {
        _teamService.GetTeamMemberAsync(TeamKey, OwnerKey).Returns(member ?? new Member());
        _userService.GetUserByKeyAsync(OwnerKey).Returns(user ?? new User());
    }

    private async Task<AuthenticateResult> Authenticate(bool withUserService = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[ApiKeyConstants.HeaderName] = Secret;

        var optionsMonitor = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        optionsMonitor.Get(ApiKeyConstants.SchemeName).Returns(new AuthenticationSchemeOptions());
        var loggerFactory = Substitute.For<ILoggerFactory>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

        var handler = new ApiKeyAuthenticationHandler(optionsMonitor, loggerFactory, UrlEncoder.Default, _apiKeyService,
            _scopes, teamService: _teamService, userService: withUserService ? _userService : null);
        await handler.InitializeAsync(new AuthenticationScheme(ApiKeyConstants.SchemeName, "API Key", typeof(ApiKeyAuthenticationHandler)), context);
        return await handler.AuthenticateAsync();
    }

    private static string[] Scopes(AuthenticateResult result)
        => result.Principal.FindAll(TeamClaimTypes.Scope).Select(x => x.Value).OrderBy(x => x).ToArray();

    // ---- The owner can still act: the key works, as the owner ----

    [Fact]
    public async Task ActiveOwner_Authenticates_WithOwnerClaim()
    {
        GivenKey();
        GivenOwner();

        var result = await Authenticate();

        Assert.True(result.Succeeded);
        Assert.Equal(OwnerKey, result.Principal.FindFirst(TeamClaimTypes.ApiKeyOwner)?.Value);
        Assert.Null(result.Principal.FindFirst(TeamClaimTypes.MemberKey));
        Assert.Equal([Read, Write], Scopes(result));
    }

    // ---- The owner can no longer act: the key is refused ----

    public static TheoryData<string> RefusalCases => ["gone", "invited", "rejected", "suspended", "no-level", "disabled", "deleted"];

    [Theory]
    [MemberData(nameof(RefusalCases))]
    public async Task OwnerWhoCannotAct_RefusesTheKey(string reason)
    {
        GivenKey();
        GivenOwner();
        switch (reason)
        {
            case "gone": _teamService.GetTeamMemberAsync(TeamKey, OwnerKey).Returns((ITeamMember)null); break;
            case "invited": GivenOwner(new Member { State = MembershipState.Invited }); break;
            case "rejected": GivenOwner(new Member { State = MembershipState.Rejected }); break;
            case "suspended": GivenOwner(new Member { SuspendedAt = DateTime.UtcNow }); break;
            case "no-level": GivenOwner(new Member { AccessLevel = null }); break;
            case "disabled": GivenOwner(user: new User { DisabledAt = DateTime.UtcNow }); break;
            case "deleted": _userService.GetUserByKeyAsync(OwnerKey).Returns((IUser)null); break;
        }

        var result = await Authenticate();

        Assert.False(result.Succeeded);
        Assert.Contains("owner", result.Failure!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LiftingASuspension_RestoresTheKey()
    {
        GivenKey();
        GivenOwner(new Member { SuspendedAt = DateTime.UtcNow });
        Assert.False((await Authenticate()).Succeeded);

        GivenOwner();

        Assert.True((await Authenticate()).Succeeded);
    }

    [Fact]
    public async Task ReEnablingTheUser_RestoresTheKey()
    {
        GivenKey();
        GivenOwner(user: new User { DisabledAt = DateTime.UtcNow });
        Assert.False((await Authenticate()).Succeeded);

        GivenOwner();

        Assert.True((await Authenticate()).Succeeded);
    }

    // ---- Fails closed ----

    [Fact]
    public async Task OwnerLookupFault_RefusesTheKey()
    {
        GivenKey();
        _teamService.GetTeamMemberAsync(TeamKey, OwnerKey).Returns<Task<ITeamMember>>(_ => throw new TimeoutException("store down"));

        var result = await Authenticate();

        Assert.False(result.Succeeded);
        Assert.Contains("could not be verified", result.Failure!.Message);
    }

    [Fact]
    public async Task NoUserServiceToVerifyWith_RefusesTheKey()
    {
        GivenKey();
        GivenOwner();

        Assert.False((await Authenticate(withUserService: false)).Succeeded);
    }

    // ---- Never more than the owner ----

    [Fact]
    public async Task KeyAboveItsOwner_IsNarrowedToTheOwner()
    {
        // An Administrator minted it at Administrator, then was demoted to Viewer.
        GivenKey(AccessLevel.Administrator);
        GivenOwner(new Member { AccessLevel = AccessLevel.Viewer });

        var result = await Authenticate();

        Assert.True(result.Succeeded);
        Assert.Equal([Read], Scopes(result));
        Assert.Equal(nameof(AccessLevel.Viewer), result.Principal.FindFirst(TeamClaimTypes.AccessLevel)?.Value);
    }

    [Fact]
    public async Task KeyBelowItsOwner_KeepsItsOwnNarrowerAccess()
    {
        GivenKey(AccessLevel.Viewer);
        GivenOwner(new Member { AccessLevel = AccessLevel.Owner });

        var result = await Authenticate();

        Assert.Equal([Read], Scopes(result));
        Assert.Equal(nameof(AccessLevel.Viewer), result.Principal.FindFirst(TeamClaimTypes.AccessLevel)?.Value);
    }

    [Fact]
    public async Task OwnersScopeOverride_DoesNotWidenTheKey_OnlyBoundsIt()
    {
        // The owner holds an extra scope through an override; the key, minted at Viewer, does not gain it.
        _scopes.Register("data:export", AccessLevel.Administrator);
        GivenKey(AccessLevel.Viewer);
        GivenOwner(new Member { AccessLevel = AccessLevel.Viewer, ScopeOverrides = ["data:export"] });

        Assert.Equal([Read], Scopes(await Authenticate()));
    }

    // ---- A team-wide key is untouched ----

    [Fact]
    public async Task TeamWideKey_NeverLooksUpAnOwner()
    {
        GivenKey(AccessLevel.Administrator, owner: null);

        var result = await Authenticate();

        Assert.True(result.Succeeded);
        Assert.Null(result.Principal.FindFirst(TeamClaimTypes.ApiKeyOwner));
        await _teamService.DidNotReceiveWithAnyArgs().GetTeamMemberAsync(default, default);
        await _userService.DidNotReceiveWithAnyArgs().GetUserByKeyAsync(default);
    }
}
