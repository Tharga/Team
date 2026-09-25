using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Pins what the startup guard reports for a team service. Two cases it exists for: a host deriving
/// <see cref="TeamServiceBase"/> directly grants <see cref="SystemTeamScopes.Read"/>, and every team page
/// then fails for the callers holding it; and a host whose store cannot look an invitation up by its code,
/// so every short invitation link it mints resolves to nothing (Tharga/Team#286).
/// </summary>
public class TeamServiceCompletenessTests
{
    private const string AllTeams = "GetAllTeamsInternalAsync";
    private const string InviteLookup = "GetTeamKeyByInviteKeyInternalAsync";

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
        protected override Task<string> GetTeamKeyByInviteKeyInternalAsync(string inviteKey) => Task.FromResult<string>(null);
    }

    /// <summary>A host extending its own base. The base's override counts.</summary>
    private sealed class DerivedFromIntermediate(IUserService userService) : IntermediateBase(userService);

    private sealed class OverridesInviteLookup(IUserService userService) : TestTeamService(userService)
    {
        protected override Task<string> GetTeamKeyByInviteKeyInternalAsync(string inviteKey) => Task.FromResult<string>(null);
    }

    [Fact]
    public void DirectService_WithTeamsReadReachable_ReportsTheGap()
    {
        var gaps = TeamServiceCompleteness.Find(typeof(DirectService), teamsReadReachable: true);

        var gap = Assert.Single(gaps, g => g.Member == AllTeams);
        Assert.Contains(SystemTeamScopes.Read, gap.Consequence);
    }

    /// <summary>
    /// Nobody can hold the scope, so nobody passes the authorization check to reach the store. Reporting it
    /// anyway is the noise that trains people to ignore startup output.
    /// </summary>
    [Fact]
    public void DirectService_WithTeamsReadUnreachable_DoesNotReportCrossTeamListing()
    {
        Assert.DoesNotContain(TeamServiceCompleteness.Find(typeof(DirectService), teamsReadReachable: false), g => g.Member == AllTeams);
    }

    [Theory]
    [InlineData(typeof(OverridesInternal))]
    [InlineData(typeof(OverridesPublic))]
    [InlineData(typeof(DerivedFromIntermediate))]
    public void AnOverrideAnywhereInTheChain_DoesNotReportCrossTeamListing(Type serviceType)
    {
        Assert.DoesNotContain(TeamServiceCompleteness.Find(serviceType, teamsReadReachable: true), g => g.Member == AllTeams);
    }

    /// <summary>
    /// Unconditional, unlike cross-team listing: every link the toolkit mints is the short form, so any host
    /// that invites anybody reaches the lookup.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoInviteLookup_ReportsTheGap_WhateverIsGranted(bool teamsReadReachable)
    {
        var gaps = TeamServiceCompleteness.Find(typeof(DirectService), teamsReadReachable);

        var gap = Assert.Single(gaps, g => g.Member == InviteLookup);
        Assert.Contains("invitation link", gap.Consequence);
    }

    [Theory]
    [InlineData(typeof(OverridesInviteLookup))]
    [InlineData(typeof(DerivedFromIntermediate))]
    public void AnInviteLookupOverrideAnywhereInTheChain_ReportsNothingForIt(Type serviceType)
    {
        Assert.DoesNotContain(TeamServiceCompleteness.Find(serviceType, teamsReadReachable: true), g => g.Member == InviteLookup);
    }

    [Fact]
    public void EverythingOverridden_ReportsNothing()
    {
        Assert.Empty(TeamServiceCompleteness.Find(typeof(DerivedFromIntermediate), teamsReadReachable: true));
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
