using Tharga.MongoDB;
using Tharga.Team;
using Tharga.Team.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The built-in store implements cross-team listing, so a host on it — directly or through its own
/// derivative — must never be told otherwise at startup.
/// </summary>
public class TeamServiceCompletenessMongoTests
{
    [Fact]
    public void DefaultTeamService_ReportsNothing()
    {
        Assert.Empty(TeamServiceCompleteness.Find(typeof(DefaultTeamService), teamsReadReachable: true));
    }

    /// <summary>A host's own service on the built-in store: the repository base's override counts.</summary>
    private sealed class HostTeamService(
        IUserService userService,
        ITeamRepository<DefaultTeamEntity, DefaultTeamMember> teamRepository,
        IMongoDbServiceFactory factory)
        : DefaultTeamService(userService, teamRepository, factory);

    [Fact]
    public void AHostDerivingTheBuiltInStore_ReportsNothing()
    {
        Assert.Empty(TeamServiceCompleteness.Find(typeof(HostTeamService), teamsReadReachable: true));
    }
}
