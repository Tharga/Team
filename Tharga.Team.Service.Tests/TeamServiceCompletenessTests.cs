using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Pins what the startup guard reports for a team service. The case it exists for: a host deriving
/// <see cref="TeamServiceBase"/> directly grants <see cref="SystemTeamScopes.Read"/>, and every team page
/// then fails for the callers holding it.
/// </summary>
public class TeamServiceCompletenessTests
{
    /// <summary>A host that overrode nothing cross-team — the reported case.</summary>
    private sealed class DirectService(IUserService userService) : TestTeamService(userService);

    private sealed class OverridesInternal(IUserService userService) : TestTeamService(userService)
    {
        protected override IAsyncEnumerable<ITeam> GetAllTeamsInternalAsync() => AsyncEnumerable.Empty<ITeam>();
    }

    /// <summary>The public member is virtual too; overriding it instead is just as complete.</summary>
    private sealed class OverridesPublic(IUserService userService) : TestTeamService(userService)
    {
        public override IAsyncEnumerable<ITeam> GetAllTeamsAsync() => AsyncEnumerable.Empty<ITeam>();
    }

    private abstract class IntermediateBase(IUserService userService) : TestTeamService(userService)
    {
        protected override IAsyncEnumerable<ITeam> GetAllTeamsInternalAsync() => AsyncEnumerable.Empty<ITeam>();
    }

    /// <summary>A host extending its own base. The base's override counts.</summary>
    private sealed class DerivedFromIntermediate(IUserService userService) : IntermediateBase(userService);

    [Fact]
    public void DirectService_WithTeamsReadReachable_ReportsTheGap()
    {
        var gaps = TeamServiceCompleteness.Find(typeof(DirectService), teamsReadReachable: true);

        var gap = Assert.Single(gaps);
        Assert.Equal("GetAllTeamsInternalAsync", gap.Member);
        Assert.Contains(SystemTeamScopes.Read, gap.Consequence);
    }

    /// <summary>
    /// Nobody can hold the scope, so nobody passes the authorization check to reach the store. Reporting it
    /// anyway is the noise that trains people to ignore startup output.
    /// </summary>
    [Fact]
    public void DirectService_WithTeamsReadUnreachable_ReportsNothing()
    {
        Assert.Empty(TeamServiceCompleteness.Find(typeof(DirectService), teamsReadReachable: false));
    }

    [Theory]
    [InlineData(typeof(OverridesInternal))]
    [InlineData(typeof(OverridesPublic))]
    [InlineData(typeof(DerivedFromIntermediate))]
    public void AnOverrideAnywhereInTheChain_ReportsNothing(Type serviceType)
    {
        Assert.Empty(TeamServiceCompleteness.Find(serviceType, teamsReadReachable: true));
    }

    /// <summary>
    /// Registration accepts any type; one that is not a <see cref="TeamServiceBase"/> has none of these
    /// extension points, so there is nothing to say about them.
    /// </summary>
    [Fact]
    public void NotATeamServiceBase_ReportsNothing()
    {
        Assert.Empty(TeamServiceCompleteness.Find(typeof(string), teamsReadReachable: true));
        Assert.Empty(TeamServiceCompleteness.Find(null, teamsReadReachable: true));
    }
}
