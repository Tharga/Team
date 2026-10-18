namespace Tharga.Team.Email.Users;

/// <summary>Who a selection reaches: the filter's matches among the active memberships of its teams, and the users added by key wherever they are active, each user once.</summary>
/// <remarks>The one place the rules are written, so the counts, the list and the mail queued all say the same.</remarks>
internal static class UserMessageAudience
{
    /// <summary>The teams the filter applies within: null for every team, empty when the selection has no filter that could reach anyone.</summary>
    /// <exception cref="ArgumentException">The selection is missing, or names neither teams nor users.</exception>
    public static string[] FilterTeamsOf(UserMessageRecipients recipients)
    {
        if (recipients == null) throw new ArgumentException("A message names who it goes to.", nameof(recipients));

        string[] teams = recipients.AllTeams ? null : [.. Clean(recipients.TeamKeys)];
        if (teams is { Length: 0 } && !Clean(recipients.UserKeys).Any())
            throw new ArgumentException("A message is written within chosen teams or every team, to users added by name, or both.", nameof(recipients));

        return HasWho(recipients) ? teams : [];
    }

    /// <summary>The users added by key, trimmed and distinct.</summary>
    public static string[] AddedOf(UserMessageRecipients recipients) => [.. Clean(recipients?.UserKeys)];

    /// <summary>The users reached, each once in the order first found — the filter's first, then those added — with the teams that brought them in and how.</summary>
    /// <remarks>A member with no access level is reached by the filter only when it reaches everyone: an unset level grants nothing, roles included. Added by key, they are reached.</remarks>
    public static UserMessageReached[] Select(IEnumerable<UserMessageMember> filterMemberships, IEnumerable<UserMessageMember> addedMemberships, UserMessageRecipients recipients)
    {
        var added = Clean(recipients.UserKeys).ToHashSet(StringComparer.Ordinal);
        var roles = Clean(recipients.Roles).ToHashSet(StringComparer.Ordinal);
        var levels = (recipients.AccessLevels ?? []).ToHashSet();

        var byFilter = Valid(filterMemberships)
            .Where(x => recipients.EveryMember || (x.AccessLevel is { } level && (levels.Contains(level) || (x.Roles ?? []).Any(roles.Contains))))
            .Select(x => (Member: x, ByFilter: true));
        var byKey = Valid(addedMemberships)
            .Where(x => added.Contains(x.UserKey))
            .Select(x => (Member: x, ByFilter: false));

        return [.. byFilter.Concat(byKey)
            .GroupBy(x => x.Member.UserKey, StringComparer.Ordinal)
            .Select(x => new UserMessageReached(
                x.First().Member,
                [.. x.Select(y => y.Member.TeamKey).Where(y => !string.IsNullOrWhiteSpace(y)).Distinct(StringComparer.Ordinal)],
                x.Any(y => y.ByFilter),
                added.Contains(x.Key)))];
    }

    /// <summary>The selection as it is recorded: trimmed and distinct, with no teams named when it applies within every team.</summary>
    public static UserMessageRecipients Normalize(UserMessageRecipients recipients) => new()
    {
        TeamKeys = recipients.AllTeams ? [] : [.. Clean(recipients.TeamKeys)],
        AllTeams = recipients.AllTeams,
        UserKeys = [.. Clean(recipients.UserKeys)],
        Roles = [.. Clean(recipients.Roles)],
        AccessLevels = [.. (recipients.AccessLevels ?? []).Distinct()],
        EveryMember = recipients.EveryMember
    };

    private static bool HasWho(UserMessageRecipients recipients)
        => recipients.EveryMember || Clean(recipients.Roles).Any() || (recipients.AccessLevels ?? []).Length > 0;

    private static IEnumerable<UserMessageMember> Valid(IEnumerable<UserMessageMember> memberships)
        => (memberships ?? []).Where(x => x != null && !string.IsNullOrWhiteSpace(x.UserKey));

    private static IEnumerable<string> Clean(IEnumerable<string> values)
        => (values ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal);
}

/// <summary>One user a selection reaches: their first membership found, the teams that brought them in, and whether the filter selected them or they were added.</summary>
internal sealed record UserMessageReached(UserMessageMember Member, string[] TeamKeys, bool ByFilter, bool Added)
{
    public string UserKey => Member.UserKey;
}
