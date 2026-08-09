using System.Security.Claims;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The two events that happen outside any service call: an interactive sign-in, and the user record created
/// as a side effect of a first one (Tharga/Team#142).
/// </summary>
/// <remarks>
/// Neither existed before. Every other audited action passes through a service the auditing decorators wrap;
/// a sign-in completes inside the authentication handshake, and a first-sign-in user record is created while
/// resolving the caller. So the audit log could say what someone did but never that they arrived — and
/// `Tharga.Team.Support` could not route the two events #142 names first, because there was nothing to route.
/// </remarks>
public class AuthAuditEntriesTests
{
    private static ClaimsPrincipal Caller(params Claim[] claims)
        => new(new ClaimsIdentity(claims, "test"));

    private sealed class TestUser : IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string Name { get; init; }
        public string EMail { get; init; }
    }

    [Fact]
    public void SignIn_IsAnAuthSuccessForAUserOnTheWeb()
    {
        var entry = AuthAuditEntries.SignIn(Caller(new Claim(ClaimTypes.Email, "a@test.com")));

        Assert.Equal(AuditEventType.AuthSuccess, entry.EventType);
        Assert.Equal("auth", entry.Feature);
        Assert.Equal("signin", entry.Action);
        Assert.True(entry.Success);
        Assert.Equal(AuditCallerType.User, entry.CallerType);
        Assert.Equal(AuditCallerSource.Web, entry.CallerSource);
        Assert.Equal("a@test.com", entry.CallerIdentity);
    }

    /// <summary>
    /// Sign-in precedes team selection, so naming a team here would be an invention. An entry that claims a
    /// team the caller had not chosen would be worse than one that claims none.
    /// </summary>
    [Fact]
    public void SignIn_NamesNoTeam()
    {
        Assert.Null(AuthAuditEntries.SignIn(Caller(new Claim(ClaimTypes.Email, "a@test.com"))).TeamKey);
    }

    /// <summary>Identity falls back through the claims an identity provider might actually supply.</summary>
    [Theory]
    [InlineData(ClaimTypes.Email)]
    [InlineData(ClaimTypes.Upn)]
    public void SignIn_FindsTheCallerFromWhicheverClaimIsPresent(string claimType)
    {
        Assert.Equal("a@test.com", AuthAuditEntries.SignIn(Caller(new Claim(claimType, "a@test.com"))).CallerIdentity);
    }

    /// <summary>A principal carrying nothing recognisable must still produce an entry, not throw.</summary>
    [Fact]
    public void SignIn_WithNothingToIdentify_StillProducesAnEntry()
    {
        var entry = AuthAuditEntries.SignIn(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Equal(AuditEventType.AuthSuccess, entry.EventType);
        Assert.True(entry.Success);
    }

    [Fact]
    public void SignIn_WithANullPrincipal_DoesNotThrow()
    {
        Assert.Equal("signin", AuthAuditEntries.SignIn(null).Action);
    }

    /// <summary>
    /// A data change, not an auth event — the sign-in is reported separately and this is a write. Keeping
    /// them as two entries is what lets a reader see that a person arrived *and* that a record appeared.
    /// </summary>
    [Fact]
    public void UserCreated_IsADataChangeCarryingTheNewUser()
    {
        var user = new TestUser { Key = "u1", Identity = "id-1", EMail = "a@test.com" };

        var entry = AuthAuditEntries.UserCreated(user, Caller(new Claim(ClaimTypes.NameIdentifier, "sub-1")));

        Assert.Equal(AuditEventType.DataChange, entry.EventType);
        Assert.Equal("auth", entry.Feature);
        Assert.Equal("user-created", entry.Action);
        Assert.Equal("id-1", entry.CallerIdentity);
        Assert.Equal("sub-1", entry.CallerUserIdentity);
        Assert.Equal("u1", entry.Metadata["user.key"]);
        Assert.Equal("a@test.com", entry.Metadata["user.email"]);
    }

    /// <summary>
    /// The actor is the new user themselves — nobody else asked for it. That is what distinguishes this from
    /// an administrator creating a user, which `AuditingUserManagementServiceDecorator` already audits.
    /// </summary>
    [Fact]
    public void UserCreated_AttributesTheNewUserAsTheActor()
    {
        var user = new TestUser { Key = "u1", Identity = "id-1" };

        Assert.Equal(AuditCallerType.User, AuthAuditEntries.UserCreated(user, Caller()).CallerType);
        Assert.Equal("id-1", AuthAuditEntries.UserCreated(user, Caller()).CallerIdentity);
    }

    [Fact]
    public void UserCreated_WithNoUser_DoesNotThrowAndCarriesNoMetadata()
    {
        var entry = AuthAuditEntries.UserCreated(null, Caller(new Claim(ClaimTypes.Email, "a@test.com")));

        Assert.Equal("user-created", entry.Action);
        Assert.Null(entry.Metadata);
        Assert.Equal("a@test.com", entry.CallerIdentity);
    }
}
