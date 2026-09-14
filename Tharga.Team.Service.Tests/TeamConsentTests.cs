using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The consent a team has in force — the one rule every consent read goes through.
/// </summary>
public class TeamConsentTests
{
    private static readonly DateTime Now = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private sealed record FakeTeam : ITeam
    {
        public string Key => "team-1";
        public string Name => "Team";
        public string Icon => null;
        public string[] ConsentedRoles { get; init; }
        public AccessLevel? ConsentAccessLevel { get; init; }
        public TemporaryConsent TemporaryConsent { get; init; }
    }

    [Fact]
    public void StandingConsent_IsInForce()
    {
        var consent = TeamConsent.Resolve(new FakeTeam { ConsentedRoles = ["Developer"], ConsentAccessLevel = AccessLevel.Viewer }, Now);

        Assert.Equal(["Developer"], consent.ConsentedRoles);
        Assert.Equal(AccessLevel.Viewer, consent.AccessLevel);
        Assert.Null(consent.ExpiresAt);
    }

    [Fact]
    public void NoConsent_IsEmptyNotNull()
    {
        var consent = TeamConsent.Resolve(new FakeTeam(), Now);

        Assert.Empty(consent.ConsentedRoles);
        Assert.False(consent.HasConsent);
    }

    [Fact]
    public void ATemporaryConsent_IsInForceUntilItExpires()
    {
        var team = new FakeTeam
        {
            ConsentedRoles = ["Developer"],
            ConsentAccessLevel = AccessLevel.Administrator,
            TemporaryConsent = new TemporaryConsent { ExpiresAt = Now.AddMinutes(1), PreviousConsentedRoles = ["Developer"], PreviousConsentAccessLevel = AccessLevel.Viewer }
        };

        var consent = TeamConsent.Resolve(team, Now);

        Assert.Equal(AccessLevel.Administrator, consent.AccessLevel);
        Assert.Equal(Now.AddMinutes(1), consent.ExpiresAt);
    }

    [Fact]
    public void AnExpiredTemporaryConsent_ReturnsToThePreviousConsent()
    {
        var team = new FakeTeam
        {
            ConsentedRoles = ["Developer"],
            ConsentAccessLevel = AccessLevel.Administrator,
            TemporaryConsent = new TemporaryConsent { ExpiresAt = Now.AddMinutes(-1), PreviousConsentedRoles = ["Developer"], PreviousConsentAccessLevel = AccessLevel.Viewer }
        };

        var consent = TeamConsent.Resolve(team, Now);

        Assert.Equal(["Developer"], consent.ConsentedRoles);
        Assert.Equal(AccessLevel.Viewer, consent.AccessLevel);
        Assert.Null(consent.ExpiresAt);
    }

    /// <summary>The team had no consent before the request was approved, so after expiry it has none again.</summary>
    [Fact]
    public void AnExpiredTemporaryConsent_WithNoPreviousConsent_LeavesNone()
    {
        var team = new FakeTeam
        {
            ConsentedRoles = ["Developer"],
            ConsentAccessLevel = AccessLevel.User,
            TemporaryConsent = new TemporaryConsent { ExpiresAt = Now.AddMinutes(-1) }
        };

        var consent = TeamConsent.Resolve(team, Now);

        Assert.False(consent.HasConsent);
        Assert.Null(consent.AccessLevel);
    }

    /// <summary>Expiry is inclusive: at the stated moment the temporary consent has ended.</summary>
    [Fact]
    public void ExpiryIsInclusive()
    {
        var team = new FakeTeam
        {
            ConsentedRoles = ["Developer"],
            TemporaryConsent = new TemporaryConsent { ExpiresAt = Now }
        };

        Assert.False(TeamConsent.Resolve(team, Now).HasConsent);
    }

    [Fact]
    public void NoTeam_HasNoConsent()
        => Assert.False(TeamConsent.Resolve(null, Now).HasConsent);

    [Fact]
    public void Covers_ACallerHoldingAConsentedRole()
        => Assert.True(new ConsentInForce(["Developer"], null, null).Covers(["Other", "Developer"]));

    [Fact]
    public void DoesNotCover_ACallerWithoutAConsentedRole()
        => Assert.False(new ConsentInForce(["Developer"], null, null).Covers(["Other"]));

    [Fact]
    public void DoesNotCover_AnyoneWhenThereIsNoConsent()
        => Assert.False(new ConsentInForce([], AccessLevel.Viewer, null).Covers(["Developer"]));

    /// <summary>Role names are compared exactly, as the store's consented-team lookup compares them.</summary>
    [Fact]
    public void RoleComparisonIsExact()
        => Assert.False(new ConsentInForce(["Developer"], null, null).Covers(["developer"]));

    [Fact]
    public void ITeamDefaults_AreNull()
    {
        ITeam team = new FakeTeam();

        Assert.Null(team.AccessRequests);
        Assert.Null(((FakeTeam)team).TemporaryConsent);
    }

    [Fact]
    public void RequestableLevels_AreViewerUserAndAdministrator()
        => Assert.Equal([AccessLevel.Viewer, AccessLevel.User, AccessLevel.Administrator], TeamAccessRequestRules.RequestableLevels);
}
