using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// <see cref="TeamGrantResolver.ConsentLevel"/> — the consent half of the grant rule, read by the team page's
/// "Your access" column so it states the same level the selected team's claims carry.
/// </summary>
public class TeamGrantConsentLevelTests
{
    private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    private sealed record ConsentTeam : ITeam
    {
        public string Key { get; init; } = "team-1";
        public string Name { get; init; } = "Team";
        public string Icon { get; init; }
        public string[] ConsentedRoles { get; init; }
        public AccessLevel? ConsentAccessLevel { get; init; }
        public TemporaryConsent TemporaryConsent { get; init; }
    }

    [Fact]
    public void CallerHoldingAConsentedRole_GetsTheConsentedLevel()
    {
        var team = new ConsentTeam { ConsentedRoles = ["Developer"], ConsentAccessLevel = AccessLevel.Administrator };

        Assert.Equal(AccessLevel.Administrator, TeamGrantResolver.ConsentLevel(team, ["Developer"], AccessLevel.Viewer, Now));
    }

    [Fact]
    public void ConsentWithoutALevel_GetsTheDefault()
    {
        var team = new ConsentTeam { ConsentedRoles = ["Developer"] };

        Assert.Equal(AccessLevel.Viewer, TeamGrantResolver.ConsentLevel(team, ["Developer"], AccessLevel.Viewer, Now));
    }

    [Fact]
    public void CallerWithoutAConsentedRole_GetsNothing()
    {
        var team = new ConsentTeam { ConsentedRoles = ["Developer"], ConsentAccessLevel = AccessLevel.Administrator };

        Assert.Null(TeamGrantResolver.ConsentLevel(team, ["User"], AccessLevel.Viewer, Now));
    }

    [Fact]
    public void TeamThatConsentedToNothing_GrantsNothing()
    {
        Assert.Null(TeamGrantResolver.ConsentLevel(new ConsentTeam(), ["Developer"], AccessLevel.Viewer, Now));
    }

    [Fact]
    public void ExpiredTemporaryConsent_FallsBackToThePreviousConsent()
    {
        var team = new ConsentTeam
        {
            ConsentedRoles = ["Developer"],
            ConsentAccessLevel = AccessLevel.Administrator,
            TemporaryConsent = new TemporaryConsent { ExpiresAt = Now, PreviousConsentedRoles = [] }
        };

        Assert.Null(TeamGrantResolver.ConsentLevel(team, ["Developer"], AccessLevel.Viewer, Now));
    }
}
