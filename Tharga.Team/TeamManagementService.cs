using System.Text.Json;
using Microsoft.Extensions.Options;
// ITeamPrincipalAccessor keeps its original namespace so no consumer's using breaks; it now lives in this
// assembly, because this is where team reads are gated and the gate needs the caller's claims.
using Tharga.Team.Service;

namespace Tharga.Team;

/// <summary>
/// Delegates to <see cref="ITeamService"/> for all operations, enforcing <c>team:read</c> on the reads.
///
/// <para>
/// <b>Mutations are enforced downstream</b>, by <c>AuthorizationTeamServiceDecorator</c> over
/// <see cref="ITeamService"/>. <b>Reads are enforced here</b>, because that decorator deliberately does
/// not gate reads: the claims pipeline reads team data while building the principal, so a gate there
/// would be circular and break sign-in.
/// </para>
/// <para>
/// The <c>[RequireScope]</c> attributes on <see cref="ITeamManagementService"/> are <b>documentation</b>.
/// They would be enforced by <c>ScopeProxy&lt;T&gt;</c> if this were registered through
/// <c>AddTeamService</c>, and it is not — so nothing derives from them at runtime. Do not add a read to
/// this class and assume the attribute covers it.
/// </para>
/// Generic methods (GetTeamsAsync, DeleteTeamAsync, RenameTeamAsync) call non-generic
/// internal versions since the proxy resolves the member type from the team data.
/// </summary>
public class TeamManagementService<TMember> : ITeamManagementService, ITeamLifecycleService, ITeamDirectoryService, ITeamOversightService, ITeamInvitationService
    where TMember : class, ITeamMember
{
    private readonly ITeamService _inner;
    private readonly IUserService _userService;
    private readonly IScopeRegistry _scopeRegistry;
    private readonly ITeamPrincipalAccessor _principalAccessor;
    private readonly ConsentOptions _consent;
    private readonly InvitationOptions _invitation;
    private readonly TeamGrantResolver _grants;

    public TeamManagementService(ITeamService inner)
        : this(inner, null, null)
    {
    }

    /// <summary>
    /// Preferred by the container when scopes are configured, so <see cref="GetTeamsAsync{T}"/> can filter
    /// per team. Falls back to the single-argument constructor when no <see cref="IScopeRegistry"/> is
    /// registered — an app not using scopes must not start refusing reads.
    /// </summary>
    public TeamManagementService(ITeamService inner, IUserService userService, IScopeRegistry scopeRegistry)
        : this(inner, userService, scopeRegistry, null, null, null)
    {
    }

    /// <summary>
    /// The full form, used by the Blazor registration. Without
    /// <paramref name="principalAccessor"/> the read gate can see membership but not consent, and without
    /// <paramref name="tenantRoleService"/> it can see access levels but not per-team custom roles.
    /// </summary>
    /// <remarks>
    /// <b>Every added dependency is optional, and each one only widens what a caller may read.</b> A host
    /// that supplies none gets the membership-and-access-level answer this service has always given, so an
    /// upgrade cannot start refusing a read that used to succeed.
    /// </remarks>
    public TeamManagementService(
        ITeamService inner,
        IUserService userService,
        IScopeRegistry scopeRegistry,
        ITeamPrincipalAccessor principalAccessor,
        ITenantRoleService tenantRoleService,
        IOptions<ConsentOptions> consentOptions,
        IOptions<InvitationOptions> invitationOptions = null)
    {
        _inner = inner;
        _userService = userService;
        _scopeRegistry = scopeRegistry;
        _principalAccessor = principalAccessor;
        _consent = consentOptions?.Value ?? new ConsentOptions();
        _invitation = invitationOptions?.Value ?? new InvitationOptions();
        _grants = new TeamGrantResolver(inner, scopeRegistry, tenantRoleService);
    }

    public Task<ITeam> CreateTeamAsync(string name = null) => _inner.CreateTeamAsync(name);
    public Task RenameTeamAsync(string teamKey, string name) => _inner.RenameTeamAsync<TMember>(teamKey, name);
    public Task DeleteTeamAsync(string teamKey) => _inner.DeleteTeamAsync<TMember>(teamKey);
    public Task AddMemberAsync(string teamKey, InviteUserModel model) => _inner.AddMemberAsync(teamKey, model);
    public Task RemoveMemberAsync(string teamKey, string userKey) => _inner.RemoveMemberAsync(teamKey, userKey);
    public Task SetMemberSuspendedAsync(string teamKey, string userKey, bool suspended)
        => _inner.SetMemberSuspendedAsync(teamKey, userKey, suspended);

    public Task SetMemberRoleAsync(string teamKey, string userKey, AccessLevel accessLevel) => _inner.SetMemberRoleAsync(teamKey, userKey, accessLevel);
    public Task ExtendInvitationAsync(string teamKey, string inviteKey) => _inner.ExtendInvitationAsync(teamKey, inviteKey);
    public Task SetMemberTenantRolesAsync(string teamKey, string userKey, string[] tenantRoles) => _inner.SetMemberTenantRolesAsync(teamKey, userKey, tenantRoles);
    public Task SetMemberScopeOverridesAsync(string teamKey, string userKey, string[] scopeOverrides) => _inner.SetMemberScopeOverridesAsync(teamKey, userKey, scopeOverrides);
    public Task SetMemberNameAsync(string teamKey, string userKey, string name) => _inner.SetMemberNameAsync(teamKey, userKey, name);
    public Task TransferOwnershipAsync(string teamKey, string newOwnerUserKey) => _inner.TransferOwnershipAsync<TMember>(teamKey, newOwnerUserKey);
    public Task SetTeamIconAsync(string teamKey, byte[] data, string contentType) => _inner.SetTeamIconAsync(teamKey, data, contentType);
    public Task ClearTeamIconAsync(string teamKey) => _inner.ClearTeamIconAsync(teamKey);
    public Task SetTeamCustomRolesAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles) => _inner.SetTeamCustomRolesAsync(teamKey, customRoles);
    public Task SetMemberLastSeenAsync(string teamKey) => _inner.SetMemberLastSeenAsync(teamKey);
    public Task SetInvitationResponseAsync(string teamKey, string userKey, string inviteCode, bool accept) => _inner.SetInvitationResponseAsync(teamKey, userKey, inviteCode, accept);
    public Task SetTeamConsentAsync(string teamKey, string[] consentedRoles, AccessLevel? accessLevel = null) => _inner.SetTeamConsentAsync(teamKey, consentedRoles, accessLevel);
    public Task<SetOwnerResult> SetOwnerAsync(string teamKey, string newOwnerUserKey) => _inner.SetOwnerAsync<TMember>(teamKey, newOwnerUserKey);

    /// <remarks>
    /// Enforced downstream on <see cref="SystemTeamScopes.Read"/> by
    /// <c>AuthorizationTeamServiceDecorator</c>, like the mutations — unlike the team-bound reads below,
    /// which the decorator deliberately does not gate.
    /// </remarks>
    public IAsyncEnumerable<ITeam> GetAllTeamsAsync() => _inner.GetAllTeamsAsync();

    /// <inheritdoc cref="GetAllTeamsAsync()"/>
    public IAsyncEnumerable<ITeam<T>> GetAllTeamsAsync<T>() where T : ITeamMember => _inner.GetAllTeamsAsync<T>();

    /// <summary>
    /// The caller's own teams, filtered to those where their membership grants <c>team:read</c>.
    /// </summary>
    /// <remarks>
    /// This one cannot carry <c>[RequireScope]</c>: it names no team, and <c>ScopeProxy</c> takes the team
    /// from the first argument. A principal also only ever holds scope claims for the *selected* team, so
    /// there is nothing in the claims to check the others against.
    /// <para>
    /// So the scopes are resolved per team, by the same <see cref="TeamGrantResolver"/> that decides
    /// <see cref="RequireTeamReadAsync"/> — through the overload that takes the member already loaded on
    /// the team, so this stays one query rather than one per team. A team whose membership does not grant
    /// <c>team:read</c> is omitted rather than returned without its roster: the scope covers "team details
    /// and members" together, and a half-visible team would be a third state nothing else in the model has.
    /// </para>
    /// <para>
    /// Consent needs no branch here. This lists the caller's <i>own</i> teams, and a team reached through
    /// consent is by definition one they are not a member of.
    /// </para>
    /// </remarks>
    public async IAsyncEnumerable<ITeam<T>> GetTeamsAsync<T>() where T : ITeamMember
    {
        var user = _userService == null ? null : await _userService.GetCurrentUserAsync();

        await foreach (var team in _inner.GetTeamsAsync<T>())
        {
            // No registry means the app does not use scopes; filtering here would refuse reads it never gated.
            if (_scopeRegistry == null || user == null)
            {
                yield return team;
                continue;
            }

            var member = team.Members == null
                ? null
                : team.Members.Select(x => (ITeamMember)x).FirstOrDefault(x => x.Key == user.Key);

            var grant = await _grants.ResolveFromMemberAsync(team.Key, member);

            if (grant?.Scopes.Contains(TeamScopes.Read) == true) yield return team;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ITeam> GetTeamsAsync()
    {
        await foreach (var team in GetTeamsAsync<TMember>())
        {
            yield return team;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsSuspendedAsync(string teamKey)
    {
        if (_userService == null) return false;

        var user = await _userService.GetCurrentUserAsync();
        if (user == null) return false;

        // Straight to the inner service, never through the gated read path: a suspended member holds no
        // scopes, so a scope-checked lookup would throw on the one question they most need answered.
        var member = await _inner.GetTeamMemberAsync(teamKey, user.Key);
        return member?.SuspendedAt != null;
    }


    /// <inheritdoc />
    /// <remarks>
    /// Reads through <c>_inner</c> deliberately: the invitee holds no scope on this team, so the gated
    /// read would refuse them. The invite code is the check, and only the invitation it names is
    /// returned — not the roster the old pattern exposed.
    /// </remarks>
    public async Task<TeamInvitation> GetInvitationAsync(string inviteCode)
    {
        if (string.IsNullOrWhiteSpace(inviteCode)) return null;

        var (teamKey, inviteKey) = await ResolveCodeAsync(inviteCode);

        // Malformed, unknown and already-used are one answer to the caller — distinguishing them
        // would confirm whether a team exists to someone who only has a link.
        if (teamKey == null || inviteKey == null) return null;

        var team = await _inner.GetTeamAsync<TMember>(teamKey);
        var invited = team?.Members?.FirstOrDefault(x => x.Invitation?.InviteKey == inviteKey);
        if (invited == null) return null;

        var user = _userService == null ? null : await _userService.GetCurrentUserAsync();
        var alreadyMember = user != null &&
            team.Members.Any(x => x.Key == user.Key && x.Invitation == null);

        // Reported rather than hidden: the screen can say "expired" and offer to ask for a new one, instead
        // of showing a join button that throws. Refusing the accept is TeamServiceBase's job, and both read
        // the same rule from InvitationPolicy so they cannot disagree.
        var expired = InvitationPolicy.HasExpired(invited.Invitation, _invitation.Lifetime, DateTime.UtcNow);

        return new TeamInvitation(team.Key, team.Name, invited.Invitation?.EMail, alreadyMember)
        {
            Status = expired ? InvitationStatus.Expired : InvitationStatus.Open,
            InviteKey = inviteKey
        };
    }

    /// <summary>
    /// The team and code an invitation link names, whichever form the link takes.
    /// </summary>
    /// <remarks>
    /// <b>Two formats, and both have to keep working.</b> Links minted from 3.20 carry a short opaque token
    /// and nothing else, resolved through the store. Links minted before that carry base64
    /// <c>{TeamKey, Code}</c> — and are sitting in inboxes right now, so dropping support for them would
    /// invalidate every invitation already sent.
    /// <para>
    /// The older form is tried first because it is self-describing: it either parses into a model naming a
    /// team or it does not. A short token is not valid base64 JSON, so it falls through to the store lookup.
    /// Anything else — a guess, a truncated link — resolves to nothing by both routes, which is the same
    /// answer either way.
    /// </para>
    /// </remarks>
    private async Task<(string TeamKey, string InviteKey)> ResolveCodeAsync(string inviteCode)
    {
        try
        {
            var invite = JsonSerializer.Deserialize<InviteModel>(Convert.FromBase64String(inviteCode));
            if (invite != null && !string.IsNullOrEmpty(invite.TeamKey) && !string.IsNullOrEmpty(invite.Code))
                return (invite.TeamKey, invite.Code);
        }
        catch
        {
            // Not the older format. That is not an error — it is the common case now.
        }

        var teamKey = await _inner.GetTeamKeyByInviteKeyAsync(inviteCode);

        return (teamKey, teamKey == null ? null : inviteCode);
    }

    /// <summary>
    /// Refuses the caller unless their membership of <paramref name="teamKey"/> grants
    /// <see cref="TeamScopes.Read"/>.
    /// </summary>
    /// <remarks>
    /// <b>Decided by <see cref="TeamGrantResolver"/>, the one copy of that rule.</b> Membership if the
    /// caller has it, the team's consent if they do not, and nothing if neither — the same answer, from the
    /// same code, that issued the caller's claims in the first place.
    /// <para>
    /// It did not always. This gate used to recompute the caller's scopes from their member row, which made
    /// two kinds of legitimate access invisible to it: a non-member reaching the team through consent, and a
    /// member whose <b>tenant role</b> grants the scope. Mutations never had the problem, because
    /// <c>AuthorizationTeamServiceDecorator</c> reads claims. So a consent-based operator could invite a
    /// member and then be refused the read that followed — the write landing and the operation still
    /// reporting "Access denied" (Tharga/Team#248).
    /// </para>
    /// <para>
    /// <b>Two escapes, and they are not the same.</b> No <see cref="IScopeRegistry"/> or no
    /// <see cref="IUserService"/> means the application does not use scopes at all — enforcing would
    /// refuse reads it never gated, so the check is skipped. A <i>resolved</i> caller who is null is the
    /// opposite: identity could not be established, and that fails closed.
    /// </para>
    /// <para>
    /// Invisible for ordinary members: <c>team:read</c> sits at <see cref="AccessLevel.Viewer"/>, so every
    /// level above inherits it. It bites <see cref="AccessLevel.Custom"/>, which is documented as carrying
    /// only its explicit grants and until now read everything anyway.
    /// </para>
    /// </remarks>
    private async Task RequireTeamReadAsync(string teamKey)
    {
        if (_scopeRegistry == null || _userService == null) return;

        var user = await _userService.GetCurrentUserAsync();
        if (user == null)
            throw new UnauthorizedAccessException(
                $"Reading team '{teamKey}' requires an authenticated caller holding '{TeamScopes.Read}'.");

        var principal = _principalAccessor == null ? null : await _principalAccessor.GetCurrentAsync();
        var grant = await _grants.ResolveAsync(principal, user.Key, teamKey, _consent.AccessLevel);

        if (grant?.Scopes.Contains(TeamScopes.Read) != true)
            throw new UnauthorizedAccessException(
                $"Reading team '{teamKey}' requires the '{TeamScopes.Read}' scope on that team.");
    }

    public async Task<ITeam<T>> GetTeamAsync<T>(string teamKey) where T : ITeamMember
    {
        await RequireTeamReadAsync(teamKey);
        return await _inner.GetTeamAsync<T>(teamKey);
    }

    public async Task<ITeam> GetTeamByKeyAsync(string teamKey)
    {
        await RequireTeamReadAsync(teamKey);
        return await _inner.GetTeamByKeyAsync(teamKey);
    }

    public async IAsyncEnumerable<ITeamMember> GetMembersAsync(string teamKey)
    {
        await RequireTeamReadAsync(teamKey);

        await foreach (var member in _inner.GetMembersAsync(teamKey))
        {
            yield return member;
        }
    }

    public async Task<ITeamMember> GetTeamMemberAsync(string teamKey, string userKey)
    {
        await RequireTeamReadAsync(teamKey);
        return await _inner.GetTeamMemberAsync(teamKey, userKey);
    }

    public async Task<IReadOnlyList<TenantRoleDefinition>> GetTeamCustomRolesAsync(string teamKey)
    {
        await RequireTeamReadAsync(teamKey);
        return await _inner.GetTeamCustomRolesAsync(teamKey);
    }
}
