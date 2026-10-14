using System.Security.Claims;
using NSubstitute;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>A message is signed with the writer's name as the product knows them, not the address their token carries.</summary>
public class SupportAuthorNameTests
{
    private const string TeamKey = "team-a";

    [Fact]
    public async Task AMessage_CarriesTheNameOnTheUsersRecord()
    {
        var users = Substitute.For<IUserService>();
        var user = Substitute.For<IUser>();
        user.Name.Returns("Alice Andersson");
        users.GetCurrentUserAsync().Returns(user);

        var raised = await Service(users).RaiseCaseAsync(TeamKey, "Subject", "Body");

        Assert.Equal("Alice Andersson", raised.AuthorName);
    }

    [Fact]
    public async Task WithNoNameOnRecord_TheTokensNameStandsIn()
    {
        var users = Substitute.For<IUserService>();
        users.GetCurrentUserAsync().Returns((IUser)null);

        var raised = await Service(users).RaiseCaseAsync(TeamKey, "Subject", "Body");

        Assert.Equal("alice@example.com", raised.AuthorName);
    }

    [Fact]
    public async Task AUserLookupThatFails_DoesNotStopTheMessage()
    {
        var users = Substitute.For<IUserService>();
        users.GetCurrentUserAsync().Returns<IUser>(_ => throw new InvalidOperationException("store down"));

        var raised = await Service(users).RaiseCaseAsync(TeamKey, "Subject", "Body");

        Assert.Equal("alice@example.com", raised.AuthorName);
    }

    private static ISupportCaseService Service(IUserService users)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "alice"),
            new(ClaimTypes.Name, "alice@example.com"),
            new(TeamClaimTypes.TeamKey, TeamKey)
        };

        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));

        return new SupportCaseService(new InMemorySupportCaseStore(), authorizer, TimeProvider.System, userService: users);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
