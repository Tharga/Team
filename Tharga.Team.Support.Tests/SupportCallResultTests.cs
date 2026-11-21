using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// What each tool returned to the model can be kept with the answer, when the host asks (Tharga/Team#545): so a stored conversation shows what the assistant
/// was told as well as what it asked, and a wrong answer can be traced to the tool or to the model.
/// </summary>
/// <remarks>
/// <b>The refusals are the tests to keep.</b> A tool's result can hold the tenant's own data, so with the option off nothing is stored, whatever a responder
/// put in the call; and it never reaches the rated-conversation feedback, which names the tools and nothing they returned.
/// </remarks>
public class SupportCallResultTests
{
    private const string Team = "team-a";
    private const string Alice = "alice-subject";
    private const string Secret = "grades-are-kept-ten-years-in-the-tenants-own-words";

    private static SupportAssistantCall Call(string result, string name = "list_retention_rules")
        => new() { Name = name, Arguments = "text: \"grades\"", Succeeded = true, Result = result };

    // --- what the service keeps ---

    [Fact]
    public void WithTheOptionOff_NothingIsKept_WhateverTheResponderPutThere()
    {
        var kept = SupportCaseService.BoundedCalls([Call(Secret)]);

        Assert.Equal(string.Empty, Assert.Single(kept).Result);
    }

    [Fact]
    public void WithTheOptionOn_AResultWithinTheLimit_IsKeptUnchanged()
    {
        var kept = SupportCaseService.BoundedCalls([Call(Secret)], storeResults: true, maxResultLength: 2000);

        Assert.Equal(Secret, Assert.Single(kept).Result);
    }

    [Fact]
    public void AResultOfExactlyTheLimit_IsNotCut()
    {
        var kept = SupportCaseService.BoundedCalls([Call("abcde")], storeResults: true, maxResultLength: 5);

        Assert.Equal("abcde", Assert.Single(kept).Result);
    }

    [Fact]
    public void AResultBeyondTheLimit_IsCut_WithAMarker()
    {
        var kept = SupportCaseService.BoundedCalls([Call("abcdefgh")], storeResults: true, maxResultLength: 5);

        Assert.Equal("abcde" + SupportAssistantOptions.CutMarker, Assert.Single(kept).Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ALimitOfNothing_KeepsNoResult(int limit)
    {
        var kept = SupportCaseService.BoundedCalls([Call(Secret)], storeResults: true, maxResultLength: limit);

        Assert.Equal(string.Empty, Assert.Single(kept).Result);
    }

    [Fact]
    public void ACallWithNoResult_KeepsNone_AndTheRestOfTheCallIsAsBefore()
    {
        var kept = SupportCaseService.BoundedCalls([Call(null)], storeResults: true);

        var call = Assert.Single(kept);
        Assert.Equal(string.Empty, call.Result);
        Assert.Equal("list_retention_rules", call.Name);
        Assert.Equal("text: \"grades\"", call.Arguments);
        Assert.True(call.Succeeded);
    }

    // --- through the service, to the stored message ---

    private sealed class World(bool store, int max = 2000)
    {
        private readonly InMemorySupportCaseStore _store = new();

        public CollectingSink Sink { get; } = new();

        public ISupportCaseService As(string subject)
        {
            var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, subject), new Claim(ClaimTypes.Name, subject), new Claim(TeamClaimTypes.TeamKey, Team)], "test"))));

            return new AuthorizationSupportCaseServiceDecorator(
                new SupportCaseService(_store, authorizer, TimeProvider.System, null, null, new Responder(), feedbackSinks: [Sink],
                    feedbackRatings: [SupportAnswerRating.Down, SupportAnswerRating.Up], storeCallResults: store, maxStoredCallResultLength: max),
                authorizer);
        }
    }

    private sealed class Responder : ISupportResponder
    {
        public Task<SupportAnswer> AnswerAsync(SupportCase supportCase, IReadOnlyList<SupportMessage> transcript, CancellationToken cancellationToken = default)
            => Task.FromResult(SupportAnswer.FromBody("Grades are kept for ten years.") with { Calls = [Call(Secret)] });
    }

    private sealed class CollectingSink : ISupportConversationFeedbackSink
    {
        public List<SupportConversationFeedback> Received { get; } = [];

        public Task HandleAsync(SupportConversationFeedback feedback, CancellationToken cancellationToken = default)
        {
            Received.Add(feedback);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }

    private static async Task<SupportCase> Answered(ISupportCaseService alice)
    {
        var raised = await alice.RaiseCaseAsync(Team, null, "How long are grades kept?", SupportAssistance.Assistant);
        await alice.RunAssistantAsync(Team, raised.Id);
        return raised;
    }

    [Fact]
    public async Task WithTheOptionOn_AStoredConversation_ShowsWhatTheToolReturned()
    {
        var alice = new World(store: true).As(Alice);
        var raised = await Answered(alice);

        var answer = (await alice.GetMessagesAsync(Team, raised.Id)).Items.Single(x => x.Kind == SupportMessageKind.Assistant);

        Assert.Equal(Secret, Assert.Single(answer.AssistantCalls).Result);
    }

    [Fact]
    public async Task WithTheOptionOff_AStoredConversation_ShowsNoResult()
    {
        var alice = new World(store: false).As(Alice);
        var raised = await Answered(alice);

        var answer = (await alice.GetMessagesAsync(Team, raised.Id)).Items.Single(x => x.Kind == SupportMessageKind.Assistant);

        Assert.Equal(string.Empty, Assert.Single(answer.AssistantCalls).Result);
    }

    /// <summary>The feedback names the tools and never what they returned, with the option on as with it off (#533).</summary>
    [Fact]
    public async Task TheRatedConversation_NeverCarriesWhatATool_Returned()
    {
        var world = new World(store: true);
        var alice = world.As(Alice);
        var raised = await Answered(alice);

        await alice.RateConversationAsync(Team, raised.Id, SupportAnswerRating.Down, "Wrong");

        var feedback = Assert.Single(world.Sink.Received);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(feedback));
        Assert.Equal([new SupportConversationFeedbackCall { Name = "list_retention_rules", Succeeded = true }], feedback.Messages.Single(x => x.Kind == SupportMessageKind.Assistant).Calls);
    }

    // --- what the toolkit's own responder puts in the call ---

    private static FunctionCallContent Asked(string id, string name = "get_my_cases") => new(id, name, new Dictionary<string, object> { ["text"] = "export" });

    [Fact]
    public void TheResponder_LeavesTheResultOut_UnlessItWasAsked()
    {
        var calls = ChatSupportResponder.CallsOf([Asked("1")], [new FunctionResultContent("1", "two cases")]);

        Assert.Equal(string.Empty, Assert.Single(calls).Result);
    }

    [Fact]
    public void TheResponder_PutsAStringResultAsItIs()
    {
        var calls = ChatSupportResponder.CallsOf([Asked("1")], [new FunctionResultContent("1", "two cases")], withResults: true);

        Assert.Equal("two cases", Assert.Single(calls).Result);
        Assert.True(calls[0].Succeeded);
    }

    [Fact]
    public void TheResponder_PutsAStructuredResultAsTheJsonTheModelWasGiven()
    {
        using var document = JsonDocument.Parse("{\"cases\":[{\"id\":\"c1\"}]}");

        var calls = ChatSupportResponder.CallsOf([Asked("1")], [new FunctionResultContent("1", document.RootElement.Clone())], withResults: true);

        Assert.Equal("{\"cases\":[{\"id\":\"c1\"}]}", Assert.Single(calls).Result);
    }

    [Fact]
    public void TheResponder_PutsAnObjectResultAsJson()
    {
        var calls = ChatSupportResponder.CallsOf([Asked("1")], [new FunctionResultContent("1", new { count = 2 })], withResults: true);

        Assert.Equal("{\"count\":2}", Assert.Single(calls).Result);
    }

    [Fact]
    public void ACallThatGotNoResultBack_HasNone_AndItsOutcomeIsNotKnown()
    {
        var calls = ChatSupportResponder.CallsOf([Asked("1")], [], withResults: true);

        var call = Assert.Single(calls);
        Assert.Equal(string.Empty, call.Result);
        Assert.Null(call.Succeeded);
    }

    [Fact]
    public void AFailedCall_IsStillMarkedFailed_AndKeepsWhatCameBack()
    {
        var calls = ChatSupportResponder.CallsOf([Asked("1")], [new FunctionResultContent("1", "refused") { Exception = new InvalidOperationException("no") }], withResults: true);

        var call = Assert.Single(calls);
        Assert.False(call.Succeeded);
        Assert.Equal("refused", call.Result);
    }

    [Fact]
    public void TheOptionsDefaults_AreOffAndTwoThousand()
    {
        var options = new SupportAssistantOptions();

        Assert.False(options.StoreCallResults);
        Assert.Equal(2000, options.MaxStoredCallResultLength);
    }
}
