using System.Security.Claims;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;
using Tharga.Team.Support.Slack;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The reason support gives when it closes a case.
/// </summary>
/// <remarks>
/// <b>The comment is for support only.</b> It is stored with the closure and shown to those who read across teams,
/// but the customer, their team and every channel never get it, so it can be written plainly.
/// </remarks>
public class CloseCommentTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Staff = "staff-subject";
    private const string Channel = "#support";
    private const string ThreadId = "1724500000.000100";
    private const string Reason = "Answered by phone, a duplicate of another case.";

    [Fact]
    public async Task ClosingWithAComment_StoresItNextToTheClosure_AttributedToWhoClosed()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Member(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        await StaffNamed(store, "Sam Support", SystemSupportScopes.AllManage).CloseCaseAsync(TeamA, raised.Id, Reason);

        var closure = (await Staffer(store).GetMessagesAsync(TeamA, raised.Id)).Items[^1];
        Assert.Equal(SupportMessageKind.System, closure.Kind);
        Assert.Equal(Reason, closure.InternalComment);
        Assert.Equal(Staff, closure.AuthorIdentity);
        Assert.Equal("Sam Support", closure.AuthorName);
        Assert.Contains("Sam Support", closure.Body);
        Assert.Equal(SupportCaseStatus.Closed, (await Staffer(store).GetCaseAsync(TeamA, raised.Id)).Status);
    }

    [Fact]
    public async Task ClosingWithoutAComment_IsAsItWasBefore()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Member(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        await Staffer(store).CloseCaseAsync(TeamA, raised.Id);

        var closure = (await Staffer(store).GetMessagesAsync(TeamA, raised.Id)).Items[^1];
        Assert.Null(closure.InternalComment);
        Assert.Equal(SupportCaseStatus.Closed, (await Staffer(store).GetCaseAsync(TeamA, raised.Id)).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankComment_ClosesWithoutOne(string comment)
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Member(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        await Staffer(store).CloseCaseAsync(TeamA, raised.Id, comment);

        Assert.Null((await Staffer(store).GetMessagesAsync(TeamA, raised.Id)).Items[^1].InternalComment);
    }

    [Fact]
    public async Task TheComment_IsTrimmed()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Member(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        await Staffer(store).CloseCaseAsync(TeamA, raised.Id, $"  {Reason}\n");

        Assert.Equal(Reason, (await Staffer(store).GetMessagesAsync(TeamA, raised.Id)).Items[^1].InternalComment);
    }

    [Fact]
    public async Task TheAuthor_DoesNotGetTheComment_ButStillSeesTheClosure()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Member(store).RaiseCaseAsync(TeamA, "Subject", "Body");
        await Staffer(store).CloseCaseAsync(TeamA, raised.Id, Reason);

        var closure = (await Member(store).GetMessagesAsync(TeamA, raised.Id)).Items[^1];

        Assert.Null(closure.InternalComment);
        Assert.Equal(SupportMessageKind.System, closure.Kind);
        Assert.Contains("closed", closure.Body);
    }

    [Fact]
    public async Task ATeamMemberReadingTheTeamsCases_DoesNotGetTheComment()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Member(store).RaiseCaseAsync(TeamA, "Subject", "Body");
        await Staffer(store).CloseCaseAsync(TeamA, raised.Id, Reason);

        var reader = Build(store, "bob-subject", memberOfTeam: TeamA, teamScopes: [SupportScopes.Read, SupportScopes.Manage]);
        var closure = (await reader.GetMessagesAsync(TeamA, raised.Id)).Items[^1];

        Assert.Null(closure.InternalComment);
    }

    [Fact]
    public async Task StaffWithReadOnlyAccessAcrossTeams_SeeTheComment()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Member(store).RaiseCaseAsync(TeamA, "Subject", "Body");
        await Staffer(store).CloseCaseAsync(TeamA, raised.Id, Reason);

        var reader = Build(store, "reader-subject", systemScopes: [SystemSupportScopes.AllRead]);

        Assert.Equal(Reason, (await reader.GetMessagesAsync(TeamA, raised.Id)).Items[^1].InternalComment);
    }

    [Fact]
    public async Task TheComment_SurvivesAReopen()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Member(store).RaiseCaseAsync(TeamA, "Subject", "Body");
        await Staffer(store).CloseCaseAsync(TeamA, raised.Id, Reason);

        await Member(store).ReopenCaseAsync(TeamA, raised.Id);

        var messages = (await Staffer(store).GetMessagesAsync(TeamA, raised.Id)).Items;
        Assert.Equal(Reason, messages.Single(x => x.InternalComment != null).InternalComment);
        Assert.Equal(SupportCaseStatus.Open, (await Staffer(store).GetCaseAsync(TeamA, raised.Id)).Status);
    }

    [Fact]
    public async Task TheComment_IsNotPostedToAnyChannel()
    {
        var store = new InMemorySupportCaseStore();
        var slack = Substitute.For<ISlackClient>();
        slack.PostAsync(Channel, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SlackPostResult.Ok(ThreadId));
        var channel = new SlackSupportChannel(slack, Options.Create(new SupportCaseOptions { SlackChannel = Channel }));
        var raised = await Member(store, channel).RaiseCaseAsync(TeamA, "Subject", "Body");

        await StaffNamed(store, "Sam Support", SystemSupportScopes.AllManage, channel).CloseCaseAsync(TeamA, raised.Id, Reason);

        foreach (var call in slack.ReceivedCalls().Where(x => x.GetMethodInfo().Name == nameof(ISlackClient.PostAsync)))
            Assert.DoesNotContain(Reason, string.Join(" ", call.GetArguments().OfType<string>()));
    }

    [Fact]
    public async Task ACaseNoTeamOwns_CanBeClosedWithAComment_ByTheQueueStaff()
    {
        var store = new InMemorySupportCaseStore();
        await store.AddCaseAsync(new SupportCase
        {
            Id = "case-unassigned",
            TeamKey = null,
            AuthorIdentity = null,
            Subject = "Cannot sign in",
            AuthorName = "stranger@example.com",
            Status = SupportCaseStatus.Open,
            CreatedAt = DateTime.UtcNow,
            MessageCount = 1
        }, new SupportMessage
        {
            Sequence = 1,
            Kind = SupportMessageKind.User,
            AuthorName = "stranger@example.com",
            Body = "It says my key expired.",
            SentAt = DateTime.UtcNow
        });
        var queue = Build(store, Staff, systemScopes: [SystemSupportScopes.Read, SystemSupportScopes.Manage]);

        await queue.CloseCaseAsync(null, "case-unassigned", "Spam.");

        Assert.Equal("Spam.", (await queue.GetMessagesAsync(null, "case-unassigned")).Items[^1].InternalComment);
    }

    [Fact]
    public async Task ClosingWithAComment_NeedsTheSameAccessAsClosing()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Member(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        var reader = Build(store, "reader-subject", systemScopes: [SystemSupportScopes.AllRead]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.CloseCaseAsync(TeamA, raised.Id, Reason));
        Assert.Equal(SupportCaseStatus.Open, (await Staffer(store).GetCaseAsync(TeamA, raised.Id)).Status);
    }

    [Fact]
    public async Task ACommentLongerThanAMessage_IsRefused_AndTheCaseStaysOpen()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Member(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Staffer(store).CloseCaseAsync(TeamA, raised.Id, new string('x', SupportCaseLimits.MaxMessageLength + 1)));

        Assert.Equal(SupportCaseStatus.Open, (await Staffer(store).GetCaseAsync(TeamA, raised.Id)).Status);
    }

    private static ISupportCaseService Member(InMemorySupportCaseStore store, params ISupportChannel[] channels)
        => Build(store, Alice, memberOfTeam: TeamA, channels: channels);

    private static ISupportCaseService Staffer(InMemorySupportCaseStore store)
        => Build(store, Staff, systemScopes: [SystemSupportScopes.AllManage]);

    private static ISupportCaseService StaffNamed(InMemorySupportCaseStore store, string displayName, string systemScope, params ISupportChannel[] channels)
        => Build(store, Staff, systemScopes: [systemScope], displayName: displayName, channels: channels);

    private static ISupportCaseService Build(
        InMemorySupportCaseStore store,
        string subject,
        string memberOfTeam = null,
        string[] teamScopes = null,
        string[] systemScopes = null,
        string displayName = null,
        ISupportChannel[] channels = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, displayName ?? subject)
        };

        if (memberOfTeam != null)
            claims.Add(new Claim(TeamClaimTypes.TeamKey, memberOfTeam));

        claims.AddRange((teamScopes ?? []).Select(s => new Claim(TeamClaimTypes.Scope, s)));
        claims.AddRange((systemScopes ?? []).Select(s => new Claim(TeamClaimTypes.SystemScope, s)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

        return new AuthorizationSupportCaseServiceDecorator(
            new SupportCaseService(store, authorizer, TimeProvider.System, channels ?? []),
            authorizer);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
