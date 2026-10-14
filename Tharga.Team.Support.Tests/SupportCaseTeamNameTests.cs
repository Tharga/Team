using System.Security.Claims;
using Microsoft.Extensions.Options;
using NSubstitute;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;
using Tharga.Team.Support.Notifications;
using Tharga.Team.Support.Slack;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// A case carries its team's name, so a message about it can say whose it is rather than quote a key — and
/// whatever somebody typed reaches Slack as text, never as markup.
/// </summary>
public class SupportCaseTeamNameTests
{
    private const string TeamKey = "team-a";
    private const string Channel = "#support";

    [Fact]
    public async Task ARaisedCase_CarriesItsTeamsName()
    {
        var store = new InMemorySupportCaseStore();

        var raised = await Service(store, Directory(("other", "Other"), (TeamKey, "Acme AB"))).RaiseCaseAsync(TeamKey, "Subject", "Body");

        Assert.Equal("Acme AB", raised.TeamName);
        Assert.Equal("Acme AB", (await store.GetCaseAsync(TeamKey, raised.Id)).TeamName);
    }

    [Fact]
    public async Task WithoutADirectory_ACaseIsStillRaised_WithNoName()
    {
        var raised = await Service(new InMemorySupportCaseStore(), directory: null).RaiseCaseAsync(TeamKey, "Subject", "Body");

        Assert.Null(raised.TeamName);
    }

    [Fact]
    public async Task ADirectoryThatFails_DoesNotStopTheCase()
    {
        var directory = Substitute.For<ITeamDirectoryService>();
        directory.GetTeamsAsync().Returns(_ => throw new InvalidOperationException("directory down"));

        var raised = await Service(new InMemorySupportCaseStore(), directory).RaiseCaseAsync(TeamKey, "Subject", "Body");

        Assert.Null(raised.TeamName);
    }

    [Fact]
    public async Task TheSlackThread_NamesTheTeam_AndEscapesWhatWasTyped()
    {
        var slack = Substitute.For<ISlackClient>();
        slack.PostAsync(default, default, default, default).ReturnsForAnyArgs(SlackPostResult.Ok("1.0"));
        var channel = new SlackSupportChannel(slack, Options.Create(new SupportCaseOptions { SlackChannel = Channel }));

        await Service(new InMemorySupportCaseStore(), Directory((TeamKey, "A & <B>")), channel).RaiseCaseAsync(TeamKey, "Hi <@channel>", "Body & more");

        await slack.Received(1).PostAsync(Channel,
            Arg.Is<string>(text => text.Contains("in A &amp; &lt;B&gt;") && text.Contains("Hi &lt;@channel&gt;") && text.Contains("Body &amp; more")),
            null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ANotification_EscapesEveryValueItSubstitutes_ButNotItsOwnTemplate()
    {
        var router = new NotificationRouter(Options.Create(new NotificationOptions
        {
            DefaultChannel = Channel,
            Routes = [new NotificationRoute { Event = "support:raise", Template = "<!here> New case in *{team.name}*." }]
        }));

        var message = Assert.Single(router.Route(new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.ServiceCall,
            Feature = "support",
            Action = "raise",
            TeamKey = TeamKey,
            Success = true,
            Metadata = new Dictionary<string, string> { [AuditMetadataKeys.TeamName] = "A & <https://evil.example|click>" }
        }));

        Assert.Equal("<!here> New case in *A &amp; &lt;https://evil.example|click&gt;*.", message.Text);
    }

    private static ITeamDirectoryService Directory(params (string Key, string Name)[] teams)
    {
        var directory = Substitute.For<ITeamDirectoryService>();
        directory.GetTeamsAsync().Returns(_ => Teams(teams));
        return directory;
    }

    private static async IAsyncEnumerable<ITeam> Teams((string Key, string Name)[] teams)
    {
        foreach (var (key, name) in teams)
        {
            var team = Substitute.For<ITeam>();
            team.Key.Returns(key);
            team.Name.Returns(name);
            yield return team;
        }

        await Task.CompletedTask;
    }

    private static ISupportCaseService Service(InMemorySupportCaseStore store, ITeamDirectoryService directory, params ISupportChannel[] channels)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "alice"),
            new(ClaimTypes.Name, "Alice"),
            new(TeamClaimTypes.TeamKey, TeamKey)
        };

        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));

        return new SupportCaseService(store, authorizer, TimeProvider.System, channels, teamDirectory: directory);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
