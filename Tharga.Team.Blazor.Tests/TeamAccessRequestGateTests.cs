using Tharga.Team;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// What the access-request UI offers, and how it says it. The service enforces every rule; these decide what is shown.
/// </summary>
public class TeamAccessRequestGateTests
{
    private static readonly TextSet Text = TextSet.Empty;

    // --- who is offered what ---

    [Fact]
    public void AMemberOfTheSelectedTeamWithManage_CanDecide()
        => Assert.True(TeamAccessRequestGate.CanDecide(hasManageScope: true, "T1", "T1", isMember: true));

    /// <summary>A manager consented in holds the scope but must not decide — approving sets the team's consent.</summary>
    [Fact]
    public void ANonMemberWithManage_CannotDecide()
        => Assert.False(TeamAccessRequestGate.CanDecide(true, "T1", "T1", isMember: false));

    /// <summary>Scope claims are issued for the selected team only, so another card's requests cannot be decided.</summary>
    [Fact]
    public void AnotherTeamThanTheSelectedOne_CannotBeDecided()
        => Assert.False(TeamAccessRequestGate.CanDecide(true, "T1", "T2", isMember: true));

    [Fact]
    public void ANonMemberHoldingAConsentRole_CanRequest()
        => Assert.True(TeamAccessRequestGate.CanRequest(isMember: false, ["Developer"], ["Developer"]));

    [Fact]
    public void AMember_CannotRequest()
        => Assert.False(TeamAccessRequestGate.CanRequest(isMember: true, ["Developer"], ["Developer"]));

    [Fact]
    public void ACallerWithoutAConsentRole_CannotRequest()
        => Assert.False(TeamAccessRequestGate.CanRequest(false, ["Support"], ["Developer"]));

    [Fact]
    public void PendingOn_FindsTheCallersPendingRequestForThatTeamOnly()
    {
        var now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
        TeamAccessRequest R(string id, TeamAccessRequestStatus status, int minutesAgo = 0)
            => new() { Id = id, RequesterKey = "me", AccessLevel = AccessLevel.User, RequestedAt = now.AddMinutes(-minutesAgo), Status = status };

        var mine = new[]
        {
            new TeamAccessRequestItem("T1", "One", R("denied", TeamAccessRequestStatus.Denied)),
            new TeamAccessRequestItem("T1", "One", R("pending", TeamAccessRequestStatus.Pending, 5)),
            new TeamAccessRequestItem("T2", "Two", R("elsewhere", TeamAccessRequestStatus.Pending))
        };

        Assert.Equal("pending", TeamAccessRequestGate.PendingOn(mine, "T1").Id);
        Assert.Null(TeamAccessRequestGate.PendingOn(mine, "T3"));
    }

    // --- wording ---

    [Theory]
    [InlineData(1, "for 1 hour")]
    [InlineData(8, "for 8 hours")]
    [InlineData(24, "for 1 day")]
    [InlineData(168, "for 7 days")]
    [InlineData(36, "for 36 hours")]
    public void DurationPhrase_ReadsNaturally(int hours, string expected)
        => Assert.Equal(expected, TeamAccessRequestGate.DurationPhrase(TimeSpan.FromHours(hours), Text));

    [Fact]
    public void DurationPhrase_ForNoEnd()
        => Assert.Equal("with no end", TeamAccessRequestGate.DurationPhrase(null, Text));

    [Fact]
    public void TheDurationChoices_EndWithNoEnd_AndAreOtherwiseAscending()
    {
        var choices = TeamAccessRequestGate.DurationChoices;

        Assert.Null(choices[^1].Duration);
        Assert.Equal(choices.Take(choices.Count - 1).Select(x => x.Duration).OrderBy(x => x), choices.Take(choices.Count - 1).Select(x => x.Duration));
    }

    /// <summary>The one sentence that tells a manager approval is not only for the requester.</summary>
    [Fact]
    public void TheApprovalWarning_SaysEveryoneWithTheRoles()
    {
        var request = new TeamAccessRequest { Id = "r", RequesterKey = "dev", AccessLevel = AccessLevel.Administrator, Duration = TimeSpan.FromHours(8), RequestedAt = DateTime.UtcNow };

        var warning = TeamAccessRequestGate.ApprovalWarning(["Developer", "Support"], request, "Acme", Text);

        Assert.StartsWith("Everyone with Developer, Support will get Administrator access to Acme for 8 hours", warning);
        Assert.Contains("not only the person who asked", warning);
    }

    private sealed record ConsentTeam : ITeam
    {
        public string Key => "T1";
        public string Name => "Team";
        public string Icon => null;
        public string[] ConsentedRoles { get; init; }
        public AccessLevel? ConsentAccessLevel { get; init; }
        public TemporaryConsent TemporaryConsent { get; init; }
    }

    [Fact]
    public void TheTemporaryConsentNotice_NamesTheLevelAndWhatItReturnsTo()
    {
        var now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
        var team = new ConsentTeam
        {
            ConsentedRoles = ["Developer"],
            ConsentAccessLevel = AccessLevel.Administrator,
            TemporaryConsent = new TemporaryConsent { ExpiresAt = now.AddHours(2), PreviousConsentedRoles = ["Developer"], PreviousConsentAccessLevel = AccessLevel.Viewer }
        };

        var notice = TeamAccessRequestGate.TemporaryConsentNotice(team, AccessLevel.Viewer, now, Text);

        Assert.StartsWith("Consent is temporary: Administrator until ", notice);
        Assert.EndsWith(", then Viewer.", notice);
    }

    [Fact]
    public void TheTemporaryConsentNotice_SaysNoAccess_WhenNothingCameBefore()
    {
        var now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
        var team = new ConsentTeam { ConsentedRoles = ["Developer"], ConsentAccessLevel = AccessLevel.User, TemporaryConsent = new TemporaryConsent { ExpiresAt = now.AddHours(1) } };

        Assert.EndsWith(", then no access.", TeamAccessRequestGate.TemporaryConsentNotice(team, AccessLevel.Viewer, now, Text));
    }

    [Fact]
    public void ThereIsNoNotice_ForStandingConsent_OrOnceTheWindowHasEnded()
    {
        var now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

        Assert.Null(TeamAccessRequestGate.TemporaryConsentNotice(new ConsentTeam { ConsentedRoles = ["Developer"] }, AccessLevel.Viewer, now, Text));
        Assert.Null(TeamAccessRequestGate.TemporaryConsentNotice(
            new ConsentTeam { ConsentedRoles = ["Developer"], TemporaryConsent = new TemporaryConsent { ExpiresAt = now.AddMinutes(-1) } }, AccessLevel.Viewer, now, Text));
    }
}
