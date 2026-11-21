using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// A key with an owner acts as its owner: its principal carries the owner's subject, so anything tied to a person finds one (Tharga/Team#547).
/// </summary>
/// <remarks>
/// <b>The refusals are the tests to keep.</b> A shared key is nobody's credential and must stay without a subject, and an owner who has gone, been suspended
/// or been disabled must still stop the key before it can speak for them. The key's own name stays the name, and the audit entry names both the person and the
/// credential, so an act can be traced to either.
/// </remarks>
public class ApiKeyOwnerSubjectTests
{
    private const string TeamKey = "acme";
    private const string OwnerKey = "user-1";
    private const string OwnerSubject = "sub-of-owner";
    private const string Secret = "the-secret";

    private readonly IApiKeyAdministrationService _apiKeyService = Substitute.For<IApiKeyAdministrationService>();
    private readonly ITeamService _teamService = Substitute.For<ITeamService>();
    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly ScopeRegistry _scopes = new();

    public ApiKeyOwnerSubjectTests()
    {
        _scopes.Register("data:read", AccessLevel.Viewer);

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
        public string Identity { get; init; } = OwnerSubject;
        public string Name { get; init; }
        public string EMail { get; init; }
        public DateTime? DisabledAt { get; init; }
    }

    private void TeamKeyOwnedBy(string owner, User user = null, Member member = null)
    {
        var key = Substitute.For<IApiKey>();
        key.Key.Returns("key-id");
        key.TeamKey.Returns(TeamKey);
        key.Name.Returns("Evaluation key");
        key.Tags.Returns(Array.Empty<Tag>());
        key.AccessLevel.Returns(AccessLevel.User);
        key.OwnerMemberKey.Returns(owner);
        _apiKeyService.GetByApiKeyAsync(Secret).Returns(key);

        if (owner == null) return;
        _teamService.GetTeamMemberAsync(TeamKey, OwnerKey).Returns(member ?? new Member());
        _userService.GetUserByKeyAsync(OwnerKey).Returns(user ?? new User());
    }

    private void SystemKeyOwnedBy(string owner, User user = null)
    {
        var key = Substitute.For<IApiKey>();
        key.Key.Returns("sys-key-id");
        key.TeamKey.Returns((string)null);
        key.Name.Returns("Evaluation system key");
        key.Tags.Returns(Array.Empty<Tag>());
        key.OwnerMemberKey.Returns(owner);
        key.SystemScopes.Returns(["support:all:read"]);
        _apiKeyService.GetByApiKeyAsync(Secret).Returns(key);

        if (owner != null) _userService.GetUserByKeyAsync(OwnerKey).Returns(user ?? new User());
    }

    private async Task<AuthenticateResult> Authenticate(UserIdentityResolver resolver = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[ApiKeyConstants.HeaderName] = Secret;

        var optionsMonitor = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        optionsMonitor.Get(ApiKeyConstants.SchemeName).Returns(new AuthenticationSchemeOptions());
        var loggerFactory = Substitute.For<ILoggerFactory>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

        var handler = new ApiKeyAuthenticationHandler(optionsMonitor, loggerFactory, UrlEncoder.Default, _apiKeyService,
            _scopes, teamService: _teamService, userService: _userService, identityResolver: resolver);
        await handler.InitializeAsync(new AuthenticationScheme(ApiKeyConstants.SchemeName, "API Key", typeof(ApiKeyAuthenticationHandler)), context);
        return await handler.AuthenticateAsync();
    }

    // --- an owned key has the owner's subject ---

    [Fact]
    public async Task APrivateTeamKey_CarriesItsOwnersSubject()
    {
        TeamKeyOwnedBy(OwnerKey);

        var result = await Authenticate();

        Assert.True(result.Succeeded);
        Assert.Equal(OwnerSubject, UserIdentityResolver.Default.GetSubject(result.Principal));
    }

    [Fact]
    public async Task APersonalSystemKey_CarriesItsOwnersSubject()
    {
        SystemKeyOwnedBy(OwnerKey);

        var result = await Authenticate();

        Assert.True(result.Succeeded);
        Assert.Equal(OwnerSubject, UserIdentityResolver.Default.GetSubject(result.Principal));
    }

    /// <summary>The same question the support service asks, answered as it is for the owner's own sign-in.</summary>
    [Fact]
    public async Task TheAuthorizer_GivesTheKeyTheOwnersSubject()
    {
        TeamKeyOwnedBy(OwnerKey);
        var result = await Authenticate();

        var subject = await new TeamAuthorizer(new FixedPrincipalAccessor(result.Principal)).GetSubjectAsync();

        Assert.Equal(OwnerSubject, subject);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }

    // --- a shared key has none ---

    [Fact]
    public async Task ASharedTeamKey_HasNoSubject()
    {
        TeamKeyOwnedBy(null);

        var result = await Authenticate();

        Assert.True(result.Succeeded);
        Assert.Null(UserIdentityResolver.Default.GetSubject(result.Principal));
        Assert.Null(result.Principal.FindFirst(ClaimTypes.NameIdentifier));
    }

    [Fact]
    public async Task ASharedSystemKey_HasNoSubject()
    {
        SystemKeyOwnedBy(null);

        var result = await Authenticate();

        Assert.True(result.Succeeded);
        Assert.Null(UserIdentityResolver.Default.GetSubject(result.Principal));
    }

    // --- nothing else about the key changes ---

    /// <summary>A case raised with a key still says it was the key, and the credential is still named, whoever it speaks for.</summary>
    [Fact]
    public async Task TheKeysOwnNameAndIdStay()
    {
        TeamKeyOwnedBy(OwnerKey);

        var principal = (await Authenticate()).Principal;

        Assert.Equal("Evaluation key", principal.FindFirst(ClaimTypes.Name)?.Value);
        Assert.Equal("key-id", principal.FindFirst(TeamClaimTypes.ApiKeyId)?.Value);
        Assert.Equal(OwnerKey, principal.FindFirst(TeamClaimTypes.ApiKeyOwner)?.Value);
    }

    /// <summary>An owner with no identity on record gives the key nothing to speak with, and it keeps working as it did.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnOwnerWithNoIdentity_GivesNoSubject_AndTheKeyStillWorks(string identity)
    {
        TeamKeyOwnedBy(OwnerKey, user: new User { Identity = identity });

        var result = await Authenticate();

        Assert.True(result.Succeeded);
        Assert.Null(UserIdentityResolver.Default.GetSubject(result.Principal));
    }

    // --- the owner must still stand behind the key ---

    [Fact]
    public async Task ADisabledOwner_StillStopsTheKey_BeforeItCanSpeakForThem()
    {
        TeamKeyOwnedBy(OwnerKey, user: new User { DisabledAt = DateTime.UtcNow });

        var result = await Authenticate();

        Assert.False(result.Succeeded);
        Assert.Null(result.Principal);
    }

    [Fact]
    public async Task ASuspendedOwner_StillStopsTheKey()
    {
        TeamKeyOwnedBy(OwnerKey, member: new Member { SuspendedAt = DateTime.UtcNow });

        Assert.False((await Authenticate()).Succeeded);
    }

    [Fact]
    public async Task ADisabledOwnerOfAPersonalSystemKey_StillStopsTheKey()
    {
        SystemKeyOwnedBy(OwnerKey, new User { DisabledAt = DateTime.UtcNow });

        Assert.False((await Authenticate()).Succeeded);
    }

    // --- the host's identity claim ---

    /// <summary>The subject is written as the claim the host reads it from, so the key resolves as the owner does wherever the host's chain looks.</summary>
    [Fact]
    public async Task WithConfiguredIdentityClaims_TheSubjectIsWrittenAsTheFirstOfThem()
    {
        var resolver = new UserIdentityResolver(["oid", ClaimTypes.NameIdentifier]);
        TeamKeyOwnedBy(OwnerKey);

        var result = await Authenticate(resolver);

        Assert.Equal(OwnerSubject, result.Principal.FindFirst("oid")?.Value);
        Assert.Equal(OwnerSubject, resolver.GetSubject(result.Principal));
        Assert.Null(result.Principal.FindFirst(ClaimTypes.NameIdentifier));
    }

    // --- the audit trail follows ---

    /// <summary>An act done with an owned key is traced to the person and to the credential, and is still an API key's act.</summary>
    [Fact]
    public async Task TheAuditEntry_NamesTheOwner_AndTheKey()
    {
        TeamKeyOwnedBy(OwnerKey);
        var principal = (await Authenticate()).Principal;
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext { User = principal });

        var entry = AuditHelper.BuildEntry(accessor, "support", "raise", "RaiseCaseAsync", 1, true, teamKey: TeamKey);

        Assert.Equal(OwnerSubject, entry.CallerUserIdentity);
        Assert.Equal("key-id", entry.CallerKeyId);
        Assert.Equal("Evaluation key", entry.CallerIdentity);
        Assert.Equal(AuditCallerType.ApiKey, entry.CallerType);
    }
}
