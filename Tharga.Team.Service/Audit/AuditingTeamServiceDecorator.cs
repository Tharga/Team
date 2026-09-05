using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Tharga.Team.Service.Audit;

/// <summary>
/// Decorator that wraps <see cref="ITeamService"/> and logs audit entries
/// for all mutation operations via <see cref="CompositeAuditLogger"/>.
/// Read operations are passed through without logging.
/// </summary>
public class AuditingTeamServiceDecorator : ITeamService
{
    private readonly ITeamService _inner;
    private readonly CompositeAuditLogger _auditLogger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    private const string Feature = "team";
    private const string ConsentNone = "none";

    public AuditingTeamServiceDecorator(ITeamService inner, CompositeAuditLogger auditLogger, IHttpContextAccessor httpContextAccessor)
    {
        _inner = inner;
        _auditLogger = auditLogger;
        _httpContextAccessor = httpContextAccessor;
    }

    public event EventHandler<TeamsListChangedEventArgs> TeamsListChangedEvent
    {
        add => _inner.TeamsListChangedEvent += value;
        remove => _inner.TeamsListChangedEvent -= value;
    }

    public event EventHandler<SelectTeamEventArgs> SelectTeamEvent
    {
        add => _inner.SelectTeamEvent += value;
        remove => _inner.SelectTeamEvent -= value;
    }

    // Read operations — pass through

    public IAsyncEnumerable<ITeam> GetTeamsAsync() => _inner.GetTeamsAsync();
    public IAsyncEnumerable<ITeam<TMember>> GetTeamsAsync<TMember>() where TMember : ITeamMember => _inner.GetTeamsAsync<TMember>();
    public Task<ITeam<TMember>> GetTeamAsync<TMember>(string teamKey) where TMember : ITeamMember => _inner.GetTeamAsync<TMember>(teamKey);
    public Task<ITeam> GetTeamByKeyAsync(string teamKey) => _inner.GetTeamByKeyAsync(teamKey);
    public Task<ITeamMember> GetTeamMemberAsync(string teamKey, string userKey) => _inner.GetTeamMemberAsync(teamKey, userKey);
    public IAsyncEnumerable<ITeamMember> GetMembersAsync(string teamKey) => _inner.GetMembersAsync(teamKey);
    public IAsyncEnumerable<ITeam> GetConsentedTeamsAsync(string[] userRoles) => _inner.GetConsentedTeamsAsync(userRoles);

    // Not audited by design: enumeration is a read with no side effect. Mutations a cross-team caller
    // performs inside a team still flow through the audited methods below.
    public IAsyncEnumerable<ITeam> GetAllTeamsAsync() => _inner.GetAllTeamsAsync();

    public IAsyncEnumerable<ITeam<TMember>> GetAllTeamsAsync<TMember>() where TMember : ITeamMember => _inner.GetAllTeamsAsync<TMember>();
    public Task SetMemberLastSeenAsync(string teamKey) => _inner.SetMemberLastSeenAsync(teamKey);
    public Task<IReadOnlyList<TenantRoleDefinition>> GetTeamCustomRolesAsync(string teamKey) => _inner.GetTeamCustomRolesAsync(teamKey);

    // Mutation operations — log audit entries

    public async Task<ITeam> CreateTeamAsync(string name)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.CreateTeamAsync(name);
            sw.Stop();
            // The resolved name, not the argument — a null argument means the service generated one.
            Log("create", nameof(CreateTeamAsync), sw.ElapsedMilliseconds, true, teamKey: result?.Key,
                metadata: Meta((AuditMetadataKeys.TeamName, result?.Name ?? name)));
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("create", nameof(CreateTeamAsync), sw.ElapsedMilliseconds, false, ex.Message,
                metadata: Meta((AuditMetadataKeys.TeamName, name)));
            throw;
        }
    }

    public async Task RenameTeamAsync<TMember>(string teamKey, string name) where TMember : ITeamMember
    {
        var previous = await TryGetTeamAsync<TMember>(teamKey);
        var metadata = Meta(
            (AuditMetadataKeys.TeamNameOld, previous?.Name),
            (AuditMetadataKeys.TeamNameNew, name));

        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.RenameTeamAsync<TMember>(teamKey, name);
            sw.Stop();
            Log("rename", nameof(RenameTeamAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey, metadata: metadata);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("rename", nameof(RenameTeamAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey, metadata);
            throw;
        }
    }

    public async Task DeleteTeamAsync<TMember>(string teamKey) where TMember : ITeamMember
    {
        // The name is unrecoverable once the team is gone, which is what earns the read here.
        var previous = await TryGetTeamAsync<TMember>(teamKey);
        var metadata = Meta((AuditMetadataKeys.TeamName, previous?.Name));

        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.DeleteTeamAsync<TMember>(teamKey);
            sw.Stop();
            Log("delete", nameof(DeleteTeamAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey, metadata: metadata);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("delete", nameof(DeleteTeamAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey, metadata);
            throw;
        }
    }

    /// <summary>
    /// Restoring is audited as its own action rather than as another "delete" — an operator reading the log
    /// has to be able to see that a deletion was undone, and by whom.
    /// </summary>
    public async Task RestoreTeamAsync<TMember>(string teamKey) where TMember : ITeamMember
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.RestoreTeamAsync<TMember>(teamKey);
            sw.Stop();
            // Read after, not before: the team is invisible to the ordinary read while it is deleted, so
            // its name is only available once it is back.
            var restored = await TryGetTeamAsync<TMember>(teamKey);
            Log("restore", nameof(RestoreTeamAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey,
                metadata: Meta((AuditMetadataKeys.TeamName, restored?.Name)));
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("restore", nameof(RestoreTeamAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey);
            throw;
        }
    }

    /// <summary>
    /// Purging is the irreversible one, so its entry is the last record that this team ever existed.
    /// </summary>
    /// <remarks>
    /// The name is read first and recorded even when the purge fails — a failed purge in a deployment
    /// without the storage privilege is exactly the event an operator will be looking for, and a key alone
    /// does not tell them which team it was.
    /// </remarks>
    public async Task PurgeTeamAsync<TMember>(string teamKey) where TMember : ITeamMember
    {
        var previous = await TryGetTeamAsync<TMember>(teamKey);
        var metadata = Meta((AuditMetadataKeys.TeamName, previous?.Name));

        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.PurgeTeamAsync<TMember>(teamKey);
            sw.Stop();
            Log("purge", nameof(PurgeTeamAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey, metadata: metadata);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("purge", nameof(PurgeTeamAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey, metadata);
            throw;
        }
    }

    public async Task AddMemberAsync(string teamKey, InviteUserModel model)
    {
        // No read: the invited identity is the whole story here.
        var inviteMetadata = Meta((AuditMetadataKeys.MemberEmail, model?.Email));

        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.AddMemberAsync(teamKey, model);
            sw.Stop();
            Log("invite", nameof(AddMemberAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey, metadata: inviteMetadata);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("invite", nameof(AddMemberAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey, inviteMetadata);
            throw;
        }
    }

    public async Task RemoveMemberAsync(string teamKey, string userKey)
    {
        var metadata = Meta((AuditMetadataKeys.MemberKey, userKey));

        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.RemoveMemberAsync(teamKey, userKey);
            sw.Stop();
            Log("remove-member", nameof(RemoveMemberAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey, metadata: metadata);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("remove-member", nameof(RemoveMemberAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey, metadata);
            throw;
        }
    }

    public async Task SetTeamIconAsync(string teamKey, byte[] data, string contentType)
    {
        var metadata = Meta(
            (AuditMetadataKeys.IconContentType, IconValidation.NormalizeContentType(contentType)),
            (AuditMetadataKeys.IconSize, data?.Length.ToString()));

        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.SetTeamIconAsync(teamKey, data, contentType);
            sw.Stop();
            Log("icon-set", nameof(SetTeamIconAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey, metadata: metadata);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("icon-set", nameof(SetTeamIconAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey, metadata);
            throw;
        }
    }

    public async Task ClearTeamIconAsync(string teamKey)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.ClearTeamIconAsync(teamKey);
            sw.Stop();
            Log("icon-clear", nameof(ClearTeamIconAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("icon-clear", nameof(ClearTeamIconAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey);
            throw;
        }
    }

    public async Task<int> RemoveUserFromAllTeamsAsync(string userKey)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var count = await _inner.RemoveUserFromAllTeamsAsync(userKey);
            sw.Stop();
            Log("remove-member-all", nameof(RemoveUserFromAllTeamsAsync), sw.ElapsedMilliseconds, true,
                metadata: Meta(
                    (AuditMetadataKeys.MemberKey, userKey),
                    (AuditMetadataKeys.MemberTeamCount, count.ToString())));
            return count;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("remove-member-all", nameof(RemoveUserFromAllTeamsAsync), sw.ElapsedMilliseconds, false, ex.Message,
                metadata: Meta((AuditMetadataKeys.MemberKey, userKey)));
            throw;
        }
    }

    /// <remarks>
    /// Distinct actions in both directions, for the same reason the key and user equivalents use them:
    /// <c>suspend</c> is a containment and <c>restore</c> is a decision to let the member back in. One
    /// entry keyed on a boolean makes "who restored this" a query rather than a reading.
    /// </remarks>
    public async Task SetMemberSuspendedAsync(string teamKey, string userKey, bool suspended)
    {
        var action = suspended ? "suspend" : "restore";
        var metadata = Meta((AuditMetadataKeys.MemberKey, userKey));

        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.SetMemberSuspendedAsync(teamKey, userKey, suspended);
            sw.Stop();
            Log(action, nameof(SetMemberSuspendedAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey, metadata: metadata);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log(action, nameof(SetMemberSuspendedAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey, metadata);
            throw;
        }
    }

    /// <remarks>
    /// Audited because it moves the window in which a link somebody already holds can be used. The invite key
    /// is deliberately <b>not</b> recorded: it is the bearer credential, and an audit trail is read by more
    /// people than may accept an invitation.
    /// </remarks>
    public async Task ExtendInvitationAsync(string teamKey, string inviteKey)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.ExtendInvitationAsync(teamKey, inviteKey);
            sw.Stop();
            Log("extend-invitation", nameof(ExtendInvitationAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("extend-invitation", nameof(ExtendInvitationAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey);
            throw;
        }
    }

    public async Task SetMemberRoleAsync(string teamKey, string userKey, AccessLevel accessLevel)
    {
        var previous = await TryGetMemberAsync(teamKey, userKey);
        var metadata = Meta(
            (AuditMetadataKeys.MemberKey, userKey),
            (AuditMetadataKeys.MemberAccessLevelOld, previous?.AccessLevel.ToString()),
            (AuditMetadataKeys.MemberAccessLevelNew, accessLevel.ToString()));

        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.SetMemberRoleAsync(teamKey, userKey, accessLevel);
            sw.Stop();
            Log("set-role", nameof(SetMemberRoleAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey, metadata: metadata);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("set-role", nameof(SetMemberRoleAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey, metadata);
            throw;
        }
    }

    public async Task SetMemberTenantRolesAsync(string teamKey, string userKey, string[] tenantRoles)
    {
        var metadata = Meta(
            (AuditMetadataKeys.MemberKey, userKey),
            (AuditMetadataKeys.MemberTenantRoles, Join(tenantRoles)));

        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.SetMemberTenantRolesAsync(teamKey, userKey, tenantRoles);
            sw.Stop();
            Log("set-tenant-roles", nameof(SetMemberTenantRolesAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey, metadata: metadata);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("set-tenant-roles", nameof(SetMemberTenantRolesAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey, metadata);
            throw;
        }
    }

    public async Task SetMemberScopeOverridesAsync(string teamKey, string userKey, string[] scopeOverrides)
    {
        var metadata = Meta(
            (AuditMetadataKeys.MemberKey, userKey),
            (AuditMetadataKeys.MemberScopeOverrides, Join(scopeOverrides)));

        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.SetMemberScopeOverridesAsync(teamKey, userKey, scopeOverrides);
            sw.Stop();
            Log("set-scope-overrides", nameof(SetMemberScopeOverridesAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey, metadata: metadata);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("set-scope-overrides", nameof(SetMemberScopeOverridesAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey, metadata);
            throw;
        }
    }

    public async Task SetMemberNameAsync(string teamKey, string userKey, string name)
    {
        // Empty string rather than null for a cleared override, so "was unset" and "was set to X" stay
        // distinguishable from a failed read (which omits the key entirely).
        var previous = await TryGetMemberAsync(teamKey, userKey);
        var metadata = Meta(
            (AuditMetadataKeys.MemberKey, userKey),
            (AuditMetadataKeys.MemberNameOld, previous == null ? null : previous.Name ?? string.Empty),
            (AuditMetadataKeys.MemberNameNew, name ?? string.Empty));

        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.SetMemberNameAsync(teamKey, userKey, name);
            sw.Stop();
            Log("set-member-name", nameof(SetMemberNameAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey, metadata: metadata);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("set-member-name", nameof(SetMemberNameAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey, metadata);
            throw;
        }
    }

    public async Task SetInvitationResponseAsync(string teamKey, string userKey, string inviteCode, bool accept)
    {
        var action = accept ? "accept-invite" : "reject-invite";
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.SetInvitationResponseAsync(teamKey, userKey, inviteCode, accept);
            sw.Stop();
            Log(action, nameof(SetInvitationResponseAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log(action, nameof(SetInvitationResponseAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey);
            throw;
        }
    }

    public async Task SetTeamConsentAsync(string teamKey, string[] consentedRoles, AccessLevel? accessLevel = null)
    {
        // "none" rather than an omitted key: consent being cleared is a fact worth recording, and is
        // distinct from a read that failed.
        var previous = await TryFindTeamAsync(teamKey);
        var metadata = Meta(
            (AuditMetadataKeys.ConsentAccessLevelOld, previous == null ? null : previous.ConsentAccessLevel?.ToString() ?? ConsentNone),
            (AuditMetadataKeys.ConsentAccessLevelNew, accessLevel?.ToString() ?? ConsentNone),
            (AuditMetadataKeys.ConsentRoles, Join(consentedRoles)));

        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.SetTeamConsentAsync(teamKey, consentedRoles, accessLevel);
            sw.Stop();
            Log("set-consent", nameof(SetTeamConsentAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey, metadata: metadata);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("set-consent", nameof(SetTeamConsentAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey, metadata);
            throw;
        }
    }

    public async Task SetTeamCustomRolesAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles)
    {
        var metadata = Meta((AuditMetadataKeys.CustomRoleNames, Join(customRoles?.Select(x => x.Name))));

        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.SetTeamCustomRolesAsync(teamKey, customRoles);
            sw.Stop();
            Log("set-custom-roles", nameof(SetTeamCustomRolesAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey, metadata: metadata);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("set-custom-roles", nameof(SetTeamCustomRolesAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey, metadata);
            throw;
        }
    }

    public async Task TransferOwnershipAsync<TMember>(string teamKey, string newOwnerUserKey) where TMember : ITeamMember
    {
        var metadata = Meta((AuditMetadataKeys.NewOwnerKey, newOwnerUserKey));

        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.TransferOwnershipAsync<TMember>(teamKey, newOwnerUserKey);
            sw.Stop();
            Log("transfer-ownership", nameof(TransferOwnershipAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey, metadata: metadata);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("transfer-ownership", nameof(TransferOwnershipAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey, metadata);
            throw;
        }
    }

    /// <remarks>
    /// A read, and deliberately not audited — it runs when a delete dialog opens, so recording it would
    /// log an entry for looking at a confirmation the operator may then cancel. The delete it precedes is
    /// audited, which is the act worth recording.
    /// </remarks>
    public Task<IReadOnlyList<ITeam>> GetTeamsForUserWithAccessLevelAsync(string userKey, AccessLevel accessLevel)
        => _inner.GetTeamsForUserWithAccessLevelAsync(userKey, accessLevel);

    /// <remarks>
    /// Audited like any ownership change, and for a stronger reason: this one hands out <c>Owner</c> and
    /// takes it away, with no sitting owner's consent. A refusal is recorded too — a rejected attempt on a
    /// team the caller has no business touching is exactly what taking one over would look like on the way
    /// in.
    /// <para>
    /// <b>A call that changes nothing writes no entry.</b> The caller is typically a sync running on a
    /// schedule, so recording "ownership set" on every pass would fill the log with events that did not
    /// happen and bury the ones that did. The demoted list is what distinguishes them, so it is read from
    /// the return value rather than guessed at.
    /// </para>
    /// </remarks>
    public async Task<SetOwnerResult> SetOwnerAsync<TMember>(string teamKey, string newOwnerUserKey) where TMember : ITeamMember
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.SetOwnerAsync<TMember>(teamKey, newOwnerUserKey);
            sw.Stop();

            // Changed, not the demoted list. Repairing an ownerless team demotes nobody and is still very
            // much an event worth recording -- keying on the list would silently stop auditing exactly the
            // case the operation was originally built for.
            if (result.Changed)
            {
                var metadata = result.DemotedOwnerKeys.Length > 0
                    ? Meta(
                        (AuditMetadataKeys.NewOwnerKey, newOwnerUserKey),
                        (AuditMetadataKeys.DemotedOwnerKeys, string.Join(",", result.DemotedOwnerKeys)))
                    : Meta((AuditMetadataKeys.NewOwnerKey, newOwnerUserKey));

                Log("set-owner", nameof(SetOwnerAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey, metadata: metadata);
            }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("set-owner", nameof(SetOwnerAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey,
                Meta((AuditMetadataKeys.NewOwnerKey, newOwnerUserKey)));
            throw;
        }
    }

    /// <summary>
    /// Builds a metadata bag, dropping pairs whose value is unknown. A failed "before" read therefore
    /// omits its key rather than recording a misleading null.
    /// </summary>
    private static Dictionary<string, string> Meta(params (string Key, string Value)[] pairs)
    {
        var metadata = new Dictionary<string, string>();
        foreach (var (key, value) in pairs)
        {
            if (value != null) metadata[key] = value;
        }
        return metadata;
    }

    private static string Join(IEnumerable<string> values) => values == null ? null : string.Join(", ", values);

    /// <summary>
    /// Reads a team by key for a "before" value. Best-effort: audit detail must never fail the operation
    /// it describes, so any error yields null and the corresponding metadata key is simply omitted.
    /// </summary>
    private async Task<ITeam> TryGetTeamAsync<TMember>(string teamKey) where TMember : ITeamMember
    {
        try
        {
            return await _inner.GetTeamAsync<TMember>(teamKey);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Exact by-key team read for the "before" value at call sites with no <c>TMember</c> to hand.
    /// Best-effort: audit detail must never fail the operation it describes, so any error yields null (the
    /// corresponding metadata key is then omitted). Unlike a scan of the caller's own teams, this also
    /// finds the team for a non-member acting through consent, so the consent "before" value is recorded
    /// for them too.
    /// </summary>
    private async Task<ITeam> TryFindTeamAsync(string teamKey)
    {
        try
        {
            return await _inner.GetTeamByKeyAsync(teamKey);
        }
        catch
        {
            return null;
        }
    }

    private async Task<ITeamMember> TryGetMemberAsync(string teamKey, string userKey)
    {
        try
        {
            await foreach (var member in _inner.GetMembersAsync(teamKey))
            {
                if (member.Key == userKey) return member;
            }
        }
        catch
        {
            // Best-effort; fall through.
        }

        return null;
    }

    private void Log(string action, string methodName, long durationMs, bool success, string errorMessage = null, string teamKey = null, IReadOnlyDictionary<string, string> metadata = null)
    {
        var entry = AuditHelper.BuildEntry(_httpContextAccessor, Feature, action, methodName, durationMs, success, errorMessage, teamKey, metadata);
        _auditLogger.Log(entry);
    }
}
