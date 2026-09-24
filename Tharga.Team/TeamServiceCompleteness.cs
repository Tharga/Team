using System.Reflection;

namespace Tharga.Team;

/// <summary>
/// A storage extension point on <see cref="TeamServiceBase"/> that a host can reach without having
/// implemented it.
/// </summary>
/// <param name="Member">The member a host is expected to override.</param>
/// <param name="Consequence">What goes wrong when it is not overridden.</param>
public sealed record TeamServiceGap(string Member, string Consequence)
{
    public override string ToString() => $"{Member} — {Consequence}";
}

/// <summary>
/// Finds storage extension points a host has left un-overridden while granting what reaches them.
/// </summary>
/// <remarks>
/// The <see cref="UserServiceCompleteness"/> counterpart for teams. A storage base such as
/// <c>TeamServiceRepositoryBase</c> overrides everything listed here, so a host on the built-in Mongo store
/// reports nothing; the gaps only appear for a host extending <see cref="TeamServiceBase"/> directly.
/// </remarks>
public static class TeamServiceCompleteness
{
    private const BindingFlags Declared =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>
    /// The gaps in <paramref name="teamServiceType"/>, filtered to what the host can actually reach.
    /// </summary>
    /// <param name="teamServiceType">The registered team service.</param>
    /// <param name="teamsReadReachable">
    /// Whether any caller can hold <see cref="SystemTeamScopes.Read"/> — through a system role, or a system
    /// API key. Without it nobody gets past the authorization check to the store, so there is nothing to report.
    /// </param>
    public static IReadOnlyList<TeamServiceGap> Find(Type teamServiceType, bool teamsReadReachable)
    {
        if (teamServiceType == null || !typeof(TeamServiceBase).IsAssignableFrom(teamServiceType)) return [];

        var gaps = new List<TeamServiceGap>();

        // Either member satisfies it: the public one is virtual too, and a host may have overridden that instead.
        if (teamsReadReachable && !Overrides(teamServiceType, "GetAllTeamsInternalAsync") && !Overrides(teamServiceType, nameof(TeamServiceBase.GetAllTeamsAsync)))
        {
            gaps.Add(new TeamServiceGap("GetAllTeamsInternalAsync",
                $"'{SystemTeamScopes.Read}' is grantable, and cross-team listing throws — every team page fails for a caller holding it"));
        }

        return gaps;
    }

    /// <summary>
    /// Whether any type between <paramref name="type"/> and <see cref="TeamServiceBase"/> declares a member
    /// named <paramref name="member"/>, any overload. Walking the chain matters: a host may extend an
    /// intermediate base of its own, and that base's override counts.
    /// </summary>
    public static bool Overrides(Type type, string member)
    {
        for (var current = type; current != null && current != typeof(TeamServiceBase); current = current.BaseType)
        {
            if (current.GetMethods(Declared).Any(m => m.Name == member)) return true;
        }

        return false;
    }
}
