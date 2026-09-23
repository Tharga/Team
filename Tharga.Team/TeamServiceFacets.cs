namespace Tharga.Team;

/// <summary>
/// The interfaces a caller injects to reach a team, all implemented by one object.
/// </summary>
/// <remarks>
/// <b>One list, so adding a facet cannot silently become a host's problem.</b> These are facets of a
/// single service — registering them individually expresses no choice, because there is exactly one
/// correct answer for each. Splitting <c>ITeamService</c> into them broke a consuming host's startup
/// twice, once at 3.5.2 and again at 3.10.0, because each split added an interface nothing registered
/// and nothing named.
/// <para>
/// Registration and the startup completeness check both read this list, so a facet added here is
/// registered and checked without either being edited.
/// </para>
/// </remarks>
public static class TeamServiceFacets
{
    /// <summary>Every interface <c>TeamManagementService&lt;TMember&gt;</c> is resolved as.</summary>
    public static IReadOnlyList<Type> All { get; } =
    [
        typeof(ITeamManagementService),
        typeof(ITeamLifecycleService),
        typeof(ITeamDirectoryService),
        typeof(ITeamOversightService),
        typeof(ITeamInvitationService),
        typeof(ITeamAccessRequestService)
    ];
}
