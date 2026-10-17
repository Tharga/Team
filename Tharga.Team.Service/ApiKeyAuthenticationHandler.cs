using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;
using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>
/// Authentication handler that validates API keys from the X-API-KEY header.
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IApiKeyAdministrationService _apiKeyAdministrationService;
    private readonly IScopeRegistry _scopeRegistry;
    private readonly IAuditLogger _auditLogger;
    private readonly ITenantRoleService _tenantRoleService;
    private readonly ITeamService _teamService;
    private readonly IUserService _userService;

    /// <summary>
    /// Creates a new instance of the API key authentication handler.
    /// </summary>
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyAdministrationService apiKeyAdministrationService,
        IScopeRegistry scopeRegistry = null,
        CompositeAuditLogger auditLogger = null,
        ITenantRoleService tenantRoleService = null,
        ITeamService teamService = null,
        IUserService userService = null)
        : base(options, logger, encoder)
    {
        _apiKeyAdministrationService = apiKeyAdministrationService;
        _scopeRegistry = scopeRegistry;
        _auditLogger = auditLogger;
        _tenantRoleService = tenantRoleService;
        _teamService = teamService;
        _userService = userService;
    }

    /// <summary>
    /// Why a personal system key must be refused, or null when its owner still stands behind it.
    /// </summary>
    /// <remarks>
    /// The system-key half of <see cref="ResolveOwnerAsync"/>: a personal key is its owner's credential, so it
    /// stops working when they are deleted or disabled, and a store fault refuses it rather than letting it
    /// through.
    /// <para>
    /// <b>What this cannot check is the owner's system role.</b> System scopes come from application roles on
    /// the sign-in token, which no store holds, so the key keeps the scopes it was created with after its owner
    /// loses the role, until someone disables or deletes it. A shared system key has the same property.
    /// </para>
    /// </remarks>
    private async Task<string> ResolveSystemOwnerAsync(IApiKey key)
    {
        if (_userService == null) return "The owner of this personal API key cannot be verified.";

        try
        {
            var user = await _userService.GetUserByKeyAsync(key.OwnerMemberKey);
            if (user == null) return "The owner of this personal API key no longer exists.";
            if (user.DisabledAt != null) return "The owner of this personal API key is disabled.";
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not verify the owner of a personal system API key. Refusing the key.");
            return "The owner of this personal API key could not be verified.";
        }
    }

    /// <summary>
    /// What a private key's owner may currently do in the key's team, or why the key must be refused.
    /// </summary>
    /// <remarks>
    /// <b>A private key is its owner's credential, so it cannot outlive or outrank them</b> (Tharga/Team#323).
    /// It used to be checked on its own: the key kept working after its owner was removed, suspended,
    /// disabled or deleted, and kept the level it was minted at after the owner was demoted.
    /// <para>
    /// <b>The grant comes from <see cref="TeamGrantResolver.ResolveFromMemberAsync"/></b>, the single copy of
    /// what a member may do, so a key and its owner's own session cannot disagree about the owner.
    /// </para>
    /// <para>
    /// <b>Fails closed, unlike <see cref="IsTeamLiveAsync"/>.</b> A store fault refuses the key. A private
    /// key is the case where a credential must stop with a person, and letting it through whenever the
    /// owner cannot be looked up is exactly the window that matters. The blast radius is also smaller:
    /// only private keys depend on this lookup, not every key on the deployment.
    /// </para>
    /// </remarks>
    private async Task<(TeamGrant Grant, string Refusal)> ResolveOwnerAsync(IApiKey key)
    {
        if (_teamService == null || _userService == null)
            return (null, "The owner of this private API key cannot be verified.");

        try
        {
            var member = await _teamService.GetTeamMemberAsync(key.TeamKey, key.OwnerMemberKey);
            if (member == null || member.State != MembershipState.Member)
                return (null, "The owner of this private API key is no longer a member of the team.");
            if (member.SuspendedAt != null)
                return (null, "The owner of this private API key is suspended from the team.");

            // The member key is the user key, so the user record is one read away.
            var user = await _userService.GetUserByKeyAsync(key.OwnerMemberKey);
            if (user == null)
                return (null, "The owner of this private API key no longer exists.");
            if (user.DisabledAt != null)
                return (null, "The owner of this private API key is disabled.");

            var grant = await new TeamGrantResolver(_teamService, _scopeRegistry, _tenantRoleService)
                .ResolveFromMemberAsync(key.TeamKey, member);
            return grant == null
                ? (null, "The owner of this private API key holds no access to the team.")
                : (grant, null);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not verify the owner of a private API key. Refusing the key.");
            return (null, "The owner of this private API key could not be verified.");
        }
    }

    /// <summary>
    /// Whether the team a key belongs to still exists.
    /// </summary>
    /// <remarks>
    /// <b>A key must not outlive its team.</b> Deleting a team soft-deletes it, and every other read in the
    /// toolkit excludes soft-deleted teams — but authentication used to look at the key alone, so a deleted
    /// team's keys kept working and kept carrying that team's scope claims. This needs no purge and no key
    /// reuse: the ordinary <c>teams:delete</c> path was enough.
    /// <para>
    /// <b>Read through <see cref="ITeamService"/> on purpose.</b> This is framework code building a
    /// principal, which is the documented case for the unchecked service — requiring a scope while issuing
    /// the claims that would grant it is circular.
    /// </para>
    /// <para>
    /// <b>Fails open on a store fault, and that is deliberate.</b> A lookup that throws is not evidence the
    /// team is gone; treating it as such would turn a transient database blip into every API key on the
    /// deployment being refused at once. <c>TeamRevalidatingAuthenticationStateProvider.IsDisabledAsync</c>
    /// reasons the same way for the same reason. A team that is genuinely deleted returns null rather than
    /// throwing, so the security case is served by the null path.
    /// </para>
    /// <para>
    /// System keys carry no team and skip this entirely — a system grant is not scoped to a tenant, so there
    /// is nothing to check.
    /// </para>
    /// </remarks>
    private async Task<bool> IsTeamLiveAsync(IApiKey key)
    {
        if (key.TeamKey == null) return true;
        if (_teamService == null) return true;

        try
        {
            return await _teamService.GetTeamByKeyAsync(key.TeamKey) != null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not confirm the team behind an API key still exists. Allowing the key rather than refusing every key on a store fault.");
            return true;
        }
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Prefer Authorization: Bearer (MCP convention; the only header most MCP clients can send).
        // Fall back to X-API-KEY so existing callers keep working.
        string apiKey = null;

        if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var auth = authHeader.ToString();
            if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                apiKey = auth["Bearer ".Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(apiKey)
            && Request.Headers.TryGetValue(ApiKeyConstants.HeaderName, out var apiKeyHeader))
            apiKey = apiKeyHeader.ToString().Trim();

        if (string.IsNullOrWhiteSpace(apiKey))
            return AuthenticateResult.NoResult();

        var key = await _apiKeyAdministrationService.GetByApiKeyAsync(apiKey);
        if (key == null)
        {
            LogAuthEvent(null, null, null, false, "Invalid API key");
            return AuthenticateResult.Fail("Invalid API key.");
        }

        // Recorded as an auth failure, not dropped. A disabled key still gets used — by a scheduled job
        // nobody remembered, or by whoever the disabling was aimed at — and those attempts are exactly
        // what an operator wants to see afterwards. A silent rejection makes the containment invisible.
        if (key.DisabledAt != null)
        {
            LogAuthEvent(key.Name ?? key.TeamKey ?? "system", key.Key, key.TeamKey, false, "Disabled API key");
            return AuthenticateResult.Fail("This API key has been disabled.");
        }

        if (!await IsTeamLiveAsync(key))
        {
            LogAuthEvent(key.Name ?? key.TeamKey, key.Key, key.TeamKey, false, "API key for a deleted team");
            return AuthenticateResult.Fail("The team this API key belongs to no longer exists.");
        }

        TeamGrant ownerGrant = null;
        if (key.TeamKey != null && !string.IsNullOrEmpty(key.OwnerMemberKey))
        {
            (ownerGrant, var refusal) = await ResolveOwnerAsync(key);
            if (refusal != null)
            {
                LogAuthEvent(key.Name ?? key.TeamKey, key.TeamKey, key.Key, false, refusal);
                return AuthenticateResult.Fail(refusal);
            }
        }

        if (key.TeamKey == null && !string.IsNullOrEmpty(key.OwnerMemberKey))
        {
            var refusal = await ResolveSystemOwnerAsync(key);
            if (refusal != null)
            {
                LogAuthEvent(key.Name ?? "system", null, key.Key, false, refusal);
                return AuthenticateResult.Fail(refusal);
            }
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, key.Name ?? key.TeamKey ?? "system"),
        };

        if (!string.IsNullOrEmpty(key.Key))
        {
            claims.Add(new Claim(TeamClaimTypes.ApiKeyId, key.Key));
        }

        if (key.TeamKey == null)
        {
            // System key: explicit scopes, no team claim. These are system grants, so they carry the
            // system claim type — a system key must not satisfy a check that asks for a scope on a team.
            claims.Add(new Claim(TeamClaimTypes.IsSystemKey, "true"));
            if (!string.IsNullOrEmpty(key.OwnerMemberKey))
                claims.Add(new Claim(TeamClaimTypes.ApiKeyOwner, key.OwnerMemberKey));
            foreach (var scope in key.SystemScopes ?? Array.Empty<string>())
            {
                claims.Add(new Claim(TeamClaimTypes.SystemScope, scope));
            }
        }
        else
        {
            // Team key: resolve scopes through registry
            var (accessLevel, roleNames, scopeOverrides) = ResolveKeyDetails(key);

            IEnumerable<string> scopes = _tenantRoleService != null
                ? await _tenantRoleService.GetEffectiveScopesAsync(key.TeamKey, accessLevel, roleNames, scopeOverrides)
                : _scopeRegistry?.GetEffectiveScopes(accessLevel, roleNames, scopeOverrides) ?? [];

            if (ownerGrant != null)
            {
                // Never more than the owner holds now: a key can narrow its owner's access, never widen it.
                scopes = scopes.Intersect(ownerGrant.Scopes, StringComparer.Ordinal);
                accessLevel = AccessLevelPrivilege.Clamp(accessLevel, ownerGrant.AccessLevel);
                claims.Add(new Claim(TeamClaimTypes.ApiKeyOwner, key.OwnerMemberKey));
            }

            claims.Add(new Claim(TeamClaimTypes.TeamKey, key.TeamKey));
            claims.Add(new Claim(TeamClaimTypes.AccessLevel, accessLevel.ToString()));

            foreach (var scope in scopes)
            {
                claims.Add(new Claim(TeamClaimTypes.Scope, scope));
            }

            foreach (var tag in key.Tags ?? Array.Empty<Tag>())
            {
                claims.Add(new Claim($"{TeamClaimTypes.TagPrefix}{tag.Key}", tag.Value));
            }
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        LogAuthEvent(key.Name ?? key.TeamKey ?? "system", key.TeamKey, key.Key, true);

        return AuthenticateResult.Success(ticket);
    }

    private void LogAuthEvent(string callerIdentity, string teamKey, string callerKeyId, bool success, string errorMessage = null)
    {
        _auditLogger?.Log(new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = success ? AuditEventType.AuthSuccess : AuditEventType.AuthFailure,
            Feature = "auth",
            Action = "apikey",
            MethodName = "HandleAuthenticateAsync",
            Success = success,
            ErrorMessage = errorMessage,
            CallerType = AuditCallerType.ApiKey,
            CallerIdentity = callerIdentity,
            CallerKeyId = callerKeyId,
            TeamKey = teamKey,
            CallerSource = AuditCallerSource.Api,
        });
    }

    private static (AccessLevel accessLevel, string[] roleNames, string[] scopeOverrides) ResolveKeyDetails(IApiKey key)
    {
        if (key is ApiKeyEntity entity)
        {
            var al = entity.AccessLevel ?? AccessLevel.Administrator;
            var roles = entity.Roles ?? Array.Empty<string>();
            var overrides = entity.ScopeOverrides ?? Array.Empty<string>();
            return (al, roles, overrides);
        }

        // Non-entity IApiKey (custom store): read the typed properties directly. (These superseded
        // the old Tags["AccessLevel"]/["TenantRoles"] fallback, which also ignored ScopeOverrides.)
        return (
            key.AccessLevel ?? AccessLevel.Viewer,
            key.Roles ?? Array.Empty<string>(),
            key.ScopeOverrides ?? Array.Empty<string>());
    }
}
