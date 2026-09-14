namespace Tharga.Team.Service.Tests;

/// <summary>
/// What approving a request records as the consent to return to.
/// </summary>
public class TeamAccessApprovalTests
{
    private static readonly DateTime Now = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static TeamAccessRequest Request(TimeSpan? duration) => new()
    {
        Id = "r1",
        RequesterKey = "dev",
        AccessLevel = AccessLevel.Administrator,
        Duration = duration,
        RequestedAt = Now.AddHours(-1)
    };

    [Fact]
    public void TheWindow_StartsAtApproval()
    {
        var (until, temporary) = TeamAccessApproval.Plan(new TestTeam(), Request(TimeSpan.FromHours(3)), Now);

        Assert.Equal(Now.AddHours(3), until);
        Assert.Equal(Now.AddHours(3), temporary.ExpiresAt);
    }

    [Fact]
    public void NoEnd_RecordsNoTemporaryConsent()
    {
        var (until, temporary) = TeamAccessApproval.Plan(new TestTeam(), Request(null), Now);

        Assert.Null(until);
        Assert.Null(temporary);
    }

    [Fact]
    public void TheStandingConsent_IsWhatItReturnsTo()
    {
        var team = new TestTeam { ConsentedRoles = ["Developer"], ConsentAccessLevel = AccessLevel.Viewer };

        var (_, temporary) = TeamAccessApproval.Plan(team, Request(TimeSpan.FromHours(1)), Now);

        Assert.Equal(["Developer"], temporary.PreviousConsentedRoles);
        Assert.Equal(AccessLevel.Viewer, temporary.PreviousConsentAccessLevel);
    }

    [Fact]
    public void NoConsentBefore_ReturnsToNone()
    {
        var (_, temporary) = TeamAccessApproval.Plan(new TestTeam(), Request(TimeSpan.FromHours(1)), Now);

        Assert.Null(temporary.PreviousConsentedRoles);
        Assert.Null(temporary.PreviousConsentAccessLevel);
    }

    /// <summary>
    /// A second window approved while the first runs keeps the team's own consent as the thing to return to — not the
    /// first window.
    /// </summary>
    [Fact]
    public void ApprovingDuringARunningWindow_KeepsTheOriginalPreviousConsent()
    {
        var team = new TestTeam
        {
            ConsentedRoles = ["Developer"],
            ConsentAccessLevel = AccessLevel.User,
            TemporaryConsent = new TemporaryConsent { ExpiresAt = Now.AddMinutes(30), PreviousConsentedRoles = ["Developer"], PreviousConsentAccessLevel = AccessLevel.Viewer }
        };

        var (_, temporary) = TeamAccessApproval.Plan(team, Request(TimeSpan.FromHours(4)), Now);

        Assert.Equal(AccessLevel.Viewer, temporary.PreviousConsentAccessLevel);
        Assert.Equal(Now.AddHours(4), temporary.ExpiresAt);
    }

    /// <summary>An earlier window that has already ended has returned the team to its previous consent; that is the base.</summary>
    [Fact]
    public void ApprovingAfterAnEndedWindow_ReturnsToWhatWasInForce()
    {
        var team = new TestTeam
        {
            ConsentedRoles = ["Developer"],
            ConsentAccessLevel = AccessLevel.Administrator,
            TemporaryConsent = new TemporaryConsent { ExpiresAt = Now.AddMinutes(-5), PreviousConsentedRoles = ["Developer"], PreviousConsentAccessLevel = AccessLevel.Viewer }
        };

        var (_, temporary) = TeamAccessApproval.Plan(team, Request(TimeSpan.FromHours(1)), Now);

        Assert.Equal(AccessLevel.Viewer, temporary.PreviousConsentAccessLevel);
    }
}
