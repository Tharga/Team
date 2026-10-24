using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The built-in tools the assistant reads with: finding a case by what it is about, through the same checks as
/// every other read.
/// </summary>
public class SupportAssistantToolsTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Bob = "bob-subject";

    [Fact]
    public async Task TheAssistantIsGiven_ItsCases_ASearch_AndATranscript()
    {
        var store = new InMemorySupportCaseStore();
        var provider = new ServiceCollection().AddSingleton(Service(store, Alice)).BuildServiceProvider();

        var tools = await new SupportAssistantTools(provider).ForCurrentCallerAsync(TeamA);

        Assert.Equal(["get_my_cases", "search_my_cases", "get_case_messages"], tools.Select(x => x.Name));
    }

    [Fact]
    public async Task ASearch_FindsACase_ByItsSubject()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Service(store, Alice);
        await alice.RaiseCaseAsync(TeamA, "Tell me a joke", "Anything will do.");
        await alice.RaiseCaseAsync(TeamA, "Export is empty", "No rows.");

        var found = Matches(await SupportAssistantTools.SearchAsync(alice, TeamA, "telling a joke", default));

        var match = Assert.Single(found);
        Assert.Equal("Tell me a joke", match.GetProperty("Subject").GetString());
    }

    [Fact]
    public async Task ASearch_FindsACase_ByWhatWasWrittenInIt_AndSaysWhere()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Service(store, Alice);
        var raised = await alice.RaiseCaseAsync(TeamA, "Something to cheer me up", "Please tell me a good joke about databases.");

        var match = Assert.Single(Matches(await SupportAssistantTools.SearchAsync(alice, TeamA, "joke", default)));

        Assert.Equal(raised.Id, match.GetProperty("Id").GetString());
        Assert.Contains("joke", match.GetProperty("Where").GetString());
    }

    /// <summary>The search reads through the service, so another member's case is never among the matches.</summary>
    [Fact]
    public async Task ASearch_FindsOnlyTheCallersOwnCases()
    {
        var store = new InMemorySupportCaseStore();
        await Service(store, Bob).RaiseCaseAsync(TeamA, "A joke for Bob", "Bob's private request.");
        var alice = Service(store, Alice);

        var result = await SupportAssistantTools.SearchAsync(alice, TeamA, "joke", default);

        Assert.Equal("None of their cases mention joke.", result);
    }

    [Fact]
    public async Task ASearchWithNoWord_SaysWhatToPass()
    {
        var result = await SupportAssistantTools.SearchAsync(Service(new InMemorySupportCaseStore(), Alice), TeamA, "a ?", default);

        Assert.Equal("Pass a word to look for, of three letters or more.", result);
    }

    [Fact]
    public async Task ASearch_ReturnsAtMostTheMaximum()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Service(store, Alice);
        for (var i = 0; i < SupportAssistantTools.MaxMatches + 3; i++) await alice.RaiseCaseAsync(TeamA, $"Export {i}", "Empty.");

        Assert.Equal(SupportAssistantTools.MaxMatches, Matches(await SupportAssistantTools.SearchAsync(alice, TeamA, "export", default)).Length);
    }

    [Theory]
    [InlineData("telling a joke", new[] { "tell", "joke" })]
    [InlineData("Jokes, exports!", new[] { "joke", "export" })]
    [InlineData("is it ok", new string[] { })]
    public void Words_AreCutToARoughStem(string text, string[] expected)
    {
        Assert.Equal(expected, SupportAssistantTools.WordsOf(text));
    }

    private static JsonElement[] Matches(object result)
        => [.. JsonSerializer.SerializeToElement(result).EnumerateArray()];

    private static ISupportCaseService Service(InMemorySupportCaseStore store, string subject)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, subject),
            new Claim(ClaimTypes.Name, subject),
            new Claim(TeamClaimTypes.TeamKey, TeamA)
        ], "test"));
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

        return new AuthorizationSupportCaseServiceDecorator(new SupportCaseService(store, authorizer, TimeProvider.System), authorizer);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
