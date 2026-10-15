using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// What a case starts as: a conversation with the assistant is the asker's own, and a support case is the
/// team's — including one that started with the assistant and was handed to a person.
/// </summary>
public class SupportCaseVisibilityDefaultsTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";

    [Fact]
    public async Task AConversationWithTheAssistant_StartsAsTheAskersOwn()
    {
        var alice = Build(new InMemorySupportCaseStore());

        var raised = await alice.RaiseCaseAsync(TeamA, null, "How do I export?", SupportAssistance.Assistant);

        Assert.Equal(SupportCaseVisibility.Private, raised.Visibility);
    }

    [Fact]
    public async Task ASupportCase_StartsAsTheTeams()
    {
        var alice = Build(new InMemorySupportCaseStore());

        var raised = await alice.RaiseCaseAsync(TeamA, null, "The export is empty.");

        Assert.Equal(SupportCaseVisibility.Team, raised.Visibility);
    }

    [Fact]
    public async Task HandingAConversationToAPerson_MakesItTheTeams()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Build(store);
        var raised = await alice.RaiseCaseAsync(TeamA, null, "How do I export?", SupportAssistance.Assistant);

        await alice.RequestHumanAsync(TeamA, raised.Id);

        Assert.Equal(SupportCaseVisibility.Team, (await alice.GetCaseAsync(TeamA, raised.Id)).Visibility);
    }

    /// <summary>Read or not, an open case support answered last waits for its author to reply or close it.</summary>
    [Fact]
    public async Task AnOpenCaseSupportAnsweredLast_IsCountedAsAnswered_UntilTheAuthorReplies()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Build(store);
        var raised = await alice.RaiseCaseAsync(TeamA, null, "The export is empty.");
        Assert.Equal(0, await alice.GetMyAnsweredCountAsync(TeamA));

        await store.AppendMessageAsync(TeamA, raised.Id, new SupportMessage { Sequence = 2, Kind = SupportMessageKind.User, AuthorIdentity = "staff", AuthorName = "Sam", Body = "Fixed.", SentAt = DateTime.UtcNow, AsSupport = true });
        await alice.MarkReadAsync(TeamA, raised.Id);
        Assert.Equal(1, await alice.GetMyAnsweredCountAsync(TeamA));

        await alice.ReplyToCaseAsync(TeamA, raised.Id, "Still empty.");
        Assert.Equal(0, await alice.GetMyAnsweredCountAsync(TeamA));
    }

    [Fact]
    public async Task AClosedCase_IsNotWaitingForAnything()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Build(store);
        var raised = await alice.RaiseCaseAsync(TeamA, null, "The export is empty.");
        await store.AppendMessageAsync(TeamA, raised.Id, new SupportMessage { Sequence = 2, Kind = SupportMessageKind.User, AuthorIdentity = "staff", AuthorName = "Sam", Body = "Fixed.", SentAt = DateTime.UtcNow, AsSupport = true });

        await alice.CloseCaseAsync(TeamA, raised.Id);

        Assert.Equal(0, await alice.GetMyAnsweredCountAsync(TeamA));
    }

    private static ISupportCaseService Build(InMemorySupportCaseStore store)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Alice),
            new(ClaimTypes.Name, Alice),
            new(TeamClaimTypes.TeamKey, TeamA)
        };
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));

        return new AuthorizationSupportCaseServiceDecorator(
            new SupportCaseService(store, authorizer, TimeProvider.System,
                responder: Substitute.For<ISupportResponder>(),
                visibilityMode: SupportCaseVisibilityMode.PerCase,
                defaultVisibility: SupportCaseVisibility.Team,
                assistantDefaultVisibility: SupportCaseVisibility.Private),
            authorizer,
            SupportCaseVisibilityMode.PerCase);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
