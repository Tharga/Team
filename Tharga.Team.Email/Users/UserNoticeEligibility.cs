namespace Tharga.Team.Email.Users;

/// <summary>Whether a member may receive a notice in their team, reading their effective team scopes only when the rule asks for a scope.</summary>
/// <remarks>
/// Scopes come from the team-aware role service when the host has dynamic roles, otherwise from the scope registry, as
/// for the member's own claims. Members holding the same level, roles and overrides in a team share one lookup.
/// </remarks>
internal sealed class UserNoticeEligibility(ITenantRoleService roles = null, IScopeRegistry scopes = null)
{
    private readonly Dictionary<string, IReadOnlyList<string>> _scopes = new(StringComparer.Ordinal);

    public async Task<bool> MayReceiveAsync(UserNoticeType notice, UserMessageMember member)
    {
        if (member == null) return false;

        var effective = notice.Audience.Kind == UserNoticeAudienceKind.Scope ? await ScopesOfAsync(member) : [];

        return notice.Audience.MayReceive(member.AccessLevel, member.Roles ?? [], effective);
    }

    private async Task<IReadOnlyList<string>> ScopesOfAsync(UserMessageMember member)
    {
        string[] parts = [member.TeamKey, member.AccessLevel.ToString(), .. (member.Roles ?? []).Order(StringComparer.Ordinal), string.Empty, .. (member.ScopeOverrides ?? []).Order(StringComparer.Ordinal)];
        var key = string.Join('\n', parts);
        if (_scopes.TryGetValue(key, out var known)) return known;

        var found = roles != null
            ? await roles.GetEffectiveScopesAsync(member.TeamKey, member.AccessLevel, member.Roles ?? [], member.ScopeOverrides ?? []) ?? []
            : scopes?.GetEffectiveScopes(member.AccessLevel, member.Roles ?? [], member.ScopeOverrides ?? []) ?? [.. member.ScopeOverrides ?? []];

        _scopes[key] = found;
        return found;
    }
}
