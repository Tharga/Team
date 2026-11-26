using System.Security.Claims;
using System.Text.Json;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The session's contract types (Tharga/Team#471): they cross the store port and the wire, so they serialize by construction, and the claim that carries the
/// session is not the identity provider's.
/// </summary>
public class UserSessionContractTests
{
    private static readonly DateTime T0 = new(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ASession_SurvivesAJsonRoundTrip()
    {
        var session = new UserSession
        {
            SessionId = "s1",
            UserKey = "user-1",
            Subject = "subject-1",
            SignedInAt = T0,
            IdentityProvider = "entra",
            AuthenticationMethods = ["pwd", "mfa"],
            Origin = SessionOrigin.Adopted,
            LastActivityAt = T0.AddMinutes(3),
            ActiveTeamKey = "acme",
            EndedAt = T0.AddHours(1),
            EndReason = SessionEndReason.NewerSignIn
        };

        var read = JsonSerializer.Deserialize<UserSession>(JsonSerializer.Serialize(session));

        Assert.NotNull(read);
        Assert.Equal((session.SessionId, session.UserKey, session.Subject, session.SignedInAt, session.IdentityProvider), (read.SessionId, read.UserKey, read.Subject, read.SignedInAt, read.IdentityProvider));
        Assert.Equal(session.AuthenticationMethods, read.AuthenticationMethods);
        Assert.Equal((session.Origin, session.LastActivityAt, session.ActiveTeamKey, session.EndedAt, session.EndReason), (read.Origin, read.LastActivityAt, read.ActiveTeamKey, read.EndedAt, read.EndReason));
    }

    [Fact]
    public void ASessionStillGoing_RoundTripsWithNoEnd()
    {
        var session = new UserSession { SessionId = "s1", UserKey = "user-1", SignedInAt = T0, Origin = SessionOrigin.SignIn };

        var read = JsonSerializer.Deserialize<UserSession>(JsonSerializer.Serialize(session));

        Assert.Null(read.EndedAt);
        Assert.Null(read.EndReason);
        Assert.Null(read.ActiveTeamKey);
    }

    [Fact]
    public void TheLimits_SurviveAJsonRoundTrip()
    {
        var limits = new SessionLimits { MaxAge = TimeSpan.FromHours(8), IdleTimeout = TimeSpan.FromMinutes(2), SingleSession = true };

        Assert.Equal(limits, JsonSerializer.Deserialize<SessionLimits>(JsonSerializer.Serialize(limits)));
    }

    [Fact]
    public void NoLimits_AreNoLimits()
    {
        Assert.Null(SessionLimits.None.MaxAge);
        Assert.Null(SessionLimits.None.IdleTimeout);
        Assert.False(SessionLimits.None.SingleSession);
    }

    [Fact]
    public void TheSessionClaim_IsNotTheIdentityProvidersSid()
    {
        Assert.NotEqual("sid", TeamClaimTypes.SessionId);
        Assert.NotEqual(ClaimTypes.Sid, TeamClaimTypes.SessionId);
        Assert.NotEmpty(TeamClaimTypes.SessionId);
    }

    [Fact]
    public void TheReasons_AreTheFiveTheDesignNames()
    {
        Assert.Equal(["Ended", "Idle", "MaxAge", "NewerSignIn", "SignedOut"], Enum.GetNames<SessionEndReason>().Order());
        Assert.Equal(["Adopted", "Host", "SignIn"], Enum.GetNames<SessionOrigin>().Order());
    }

    // ---- the notifications (Tharga/Team#471 part 2): they cross a host's backplane, so they serialize by construction

    [Fact]
    public void TheEndNotification_SurvivesAJsonRoundTrip()
    {
        var args = new UserSessionEndedEventArgs { UserKey = "user-1", Reason = SessionEndReason.NewerSignIn, FromRemote = true };

        var read = JsonSerializer.Deserialize<UserSessionEndedEventArgs>(JsonSerializer.Serialize(args));

        Assert.NotNull(read);
        Assert.Equal(("user-1", SessionEndReason.NewerSignIn, true), (read.UserKey, read.Reason, read.FromRemote));
    }

    [Fact]
    public void TheEndNotification_IsNotRemote_UntilARelaySaysSo()
    {
        var args = new UserSessionEndedEventArgs { UserKey = "user-1", Reason = SessionEndReason.Idle };

        Assert.False(args.FromRemote);
        Assert.False(JsonSerializer.Deserialize<UserSessionEndedEventArgs>("{\"UserKey\":\"u\",\"Reason\":2}").FromRemote);
    }

    /// <summary>It names the person and why. A session id is the credential's key and must not travel to listeners, or over a backplane.</summary>
    [Fact]
    public void TheEndNotification_CarriesNoSession()
    {
        var args = new UserSessionEndedEventArgs { UserKey = "user-1", Reason = SessionEndReason.Idle };

        var members = typeof(UserSessionEndedEventArgs).GetProperties().Select(x => x.Name).Order().ToArray();
        var json = JsonSerializer.SerializeToElement(args).EnumerateObject().Select(x => x.Name).Order().ToArray();

        Assert.Equal(["FromRemote", "Reason", "UserKey"], members);
        Assert.Equal(["FromRemote", "Reason", "UserKey"], json);
    }

    [Fact]
    public void TheEndNotification_CannotBeBuiltWithoutThePersonOrTheReason()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UserSessionEndedEventArgs>("{\"Reason\":1}"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UserSessionEndedEventArgs>("{\"UserKey\":\"u\"}"));
    }

    [Theory]
    [InlineData("acme")]
    [InlineData(null)]
    public void TheEntryNotification_SurvivesAJsonRoundTrip_WithOrWithoutATeam(string team)
    {
        var args = new TeamEntryInvalidatedEventArgs { UserKey = "user-1", TeamKey = team, FromRemote = true };

        var read = JsonSerializer.Deserialize<TeamEntryInvalidatedEventArgs>(JsonSerializer.Serialize(args));

        Assert.NotNull(read);
        Assert.Equal(("user-1", team, true), (read.UserKey, read.TeamKey, read.FromRemote));
    }

    [Fact]
    public void TheEntryNotification_NamesNoTeamUnlessOneWasForgotten_AndIsNotRemoteUntilARelaySaysSo()
    {
        var args = new TeamEntryInvalidatedEventArgs { UserKey = "user-1" };

        Assert.Null(args.TeamKey);
        Assert.False(args.FromRemote);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TeamEntryInvalidatedEventArgs>("{\"TeamKey\":\"acme\"}"));
    }
}
