namespace Tharga.Team.Email.Users;

/// <summary>Who a selection reaches among the active memberships of its teams: each user once, however many of the selection's parts and teams name them.</summary>
internal static class UserMessageAudience
{
    /// <summary>The teams the selection applies within, distinct, or empty when it applies within every team.</summary>
    /// <exception cref="ArgumentException">The selection is missing, or names neither teams nor every team.</exception>
    public static string[] TeamsOf(UserMessageRecipients recipients)
    {
        if (recipients == null) throw new ArgumentException("A message names who it goes to.", nameof(recipients));
        if (recipients.AllTeams) return [];

        string[] teams = [.. Clean(recipients.TeamKeys)];
        if (teams.Length == 0) throw new ArgumentException("A message is written within chosen teams, or within every team.", nameof(recipients));

        return teams;
    }

    /// <summary>The users the selection reaches among <paramref name="memberships"/>, each once, in the order first found.</summary>
    public static UserMessageMember[] Select(IEnumerable<UserMessageMember> memberships, UserMessageRecipients recipients)
    {
        var users = Clean(recipients.UserKeys).ToHashSet(StringComparer.Ordinal);
        var roles = Clean(recipients.Roles).ToHashSet(StringComparer.Ordinal);
        var levels = (recipients.AccessLevels ?? []).ToHashSet();

        return [.. (memberships ?? [])
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.UserKey))
            .Where(x => recipients.EveryMember || users.Contains(x.UserKey) || levels.Contains(x.AccessLevel) || (x.Roles ?? []).Any(roles.Contains))
            .GroupBy(x => x.UserKey, StringComparer.Ordinal)
            .Select(x => x.First())];
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

    private static IEnumerable<string> Clean(IEnumerable<string> values)
        => (values ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal);
}
