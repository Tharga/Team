using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Who may do what to a support case.
/// </summary>
/// <remarks>
/// <b>The cross-tenant test is the one that earns its place.</b> A case id is a guessable, quotable,
/// forwardable string that will end up in Slack messages and URLs, so "holds a valid id" must never be
/// sufficient. Every store method takes the team for that reason, and this asserts the shape actually
/// delivers it rather than merely encouraging it.
/// <para>
/// The rest pin the deliberate asymmetry: raising a case and reading your own are authorized by membership
/// and authorship, because a scope every host must grant to everybody checks nothing — while reading
/// somebody else's case needs a real grant.
/// </para>
/// </remarks>
public class SupportCaseAuthorizationTests
{
    private const string TeamA = "team-a";
    private const string TeamB = "team-b";
    private const string Alice = "alice-subject";
    private const string Bob = "bob-subject";

    [Fact]
    public async Task AMember_CanRaiseACase_WithoutHoldingAnyScope()
    {
        var service = Build(TeamA, Alice);

        var raised = await service.RaiseCaseAsync(TeamA, "Cannot sign in", "It says my key expired.");

        Assert.Equal(TeamA, raised.TeamKey);
        Assert.Equal(Alice, raised.AuthorIdentity);
        Assert.Equal(SupportCaseStatus.Open, raised.Status);
        Assert.Equal(1, raised.MessageCount);
    }

    [Fact]
    public async Task ANonMember_CannotRaiseACase()
    {
        var service = Build(TeamB, Alice);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.RaiseCaseAsync(TeamA, "Subject", "Body"));
    }

    [Fact]
    public async Task TheAuthor_CanReadAndReplyToTheirOwnCase_WithoutAScope()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Build(TeamA, Alice, store);

        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");

        await alice.ReplyToCaseAsync(TeamA, raised.Id, "Any news?");

        var messages = await alice.GetMessagesAsync(TeamA, raised.Id);
        Assert.Equal(2, messages.Items.Length);
    }

    [Fact]
    public async Task AnotherMember_CannotReadSomeoneElsesCase_WithoutAScope()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Build(TeamA, Alice, store).RaiseCaseAsync(TeamA, "Subject", "Body");

        var bob = Build(TeamA, Bob, store);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bob.GetCaseAsync(TeamA, raised.Id));
    }

    [Fact]
    public async Task AMemberHoldingSupportRead_CanReadSomeoneElsesCase()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Build(TeamA, Alice, store).RaiseCaseAsync(TeamA, "Subject", "Body");

        var agent = Build(TeamA, Bob, store, scopes: [SupportScopes.Read]);

        var read = await agent.GetCaseAsync(TeamA, raised.Id);

        Assert.Equal(raised.Id, read.Id);
    }

    [Fact]
    public async Task ListingEveryCaseInTheTeam_RequiresSupportRead()
    {
        var store = new InMemorySupportCaseStore();
        await Build(TeamA, Alice, store).RaiseCaseAsync(TeamA, "Subject", "Body");

        var bob = Build(TeamA, Bob, store);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bob.GetCasesAsync(TeamA));

        var agent = Build(TeamA, Bob, store, scopes: [SupportScopes.Read]);
        var all = await agent.GetCasesAsync(TeamA);
        Assert.Single(all.Items);
    }

    /// <summary>
    /// Holding a valid case id from another tenant must gain nothing, even for a caller who is a fully
    /// privileged member of their own team.
    /// </summary>
    [Fact]
    public async Task AValidCaseIdFromAnotherTeam_IsRefused()
    {
        var store = new InMemorySupportCaseStore();
        var raisedInA = await Build(TeamA, Alice, store).RaiseCaseAsync(TeamA, "Subject", "Body");

        var privilegedInB = Build(TeamB, Bob, store, scopes: [SupportScopes.Read, SupportScopes.Manage]);

        // Asking team B for team A's case must not resolve it...
        Assert.Null(await privilegedInB.GetCaseAsync(TeamB, raisedInA.Id));

        // ...and naming team A while being a member of team B must be refused outright.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => privilegedInB.GetCaseAsync(TeamA, raisedInA.Id));
    }

    /// <summary>
    /// The lifetime requirement: a case outlives its author, and stays readable with the name it was written
    /// with.
    /// </summary>
    [Fact]
    public async Task ACaseOutlivesItsAuthor_AndKeepsTheNameSnapshot()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Build(TeamA, Alice, store, displayName: "Alice Example").RaiseCaseAsync(TeamA, "Subject", "Body");

        // The author is deleted: nothing rewrites the case, and no membership remains anywhere.
        var agent = Build(TeamA, Bob, store, scopes: [SupportScopes.Read]);

        var read = await agent.GetCaseAsync(TeamA, raised.Id);

        Assert.Equal("Alice Example", read.AuthorName);
        Assert.Equal(Alice, read.AuthorIdentity);

        var messages = await agent.GetMessagesAsync(TeamA, raised.Id);
        Assert.Equal("Alice Example", messages.Items[0].AuthorName);
    }

    [Fact]
    public async Task ClosingACase_RecordsTheClosureInTheTranscript()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Build(TeamA, Alice, store);
        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");

        await alice.CloseCaseAsync(TeamA, raised.Id);

        var read = await alice.GetCaseAsync(TeamA, raised.Id);
        Assert.Equal(SupportCaseStatus.Closed, read.Status);

        var messages = await alice.GetMessagesAsync(TeamA, raised.Id);
        Assert.Equal(SupportMessageKind.System, messages.Items[^1].Kind);
    }

    [Fact]
    public async Task ACaseRaisedOnTheSite_HasNoChannelBindings()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Build(TeamA, Alice, store).RaiseCaseAsync(TeamA, "Subject", "Body");

        var read = await Build(TeamA, Alice, store).GetCaseAsync(TeamA, raised.Id);

        Assert.Empty(read.Bindings);
    }

    /// <summary>
    /// One signature rather than overloads, deliberately: an overload taking a trailing <c>params</c> array
    /// alongside one taking a display name silently bound a scope as a name, which made a grant vanish and
    /// three tests fail for a reason that had nothing to do with the code under test.
    /// </summary>
    private static ISupportCaseService Build(
        string memberOfTeam,
        string subject,
        InMemorySupportCaseStore store = null,
        string[] scopes = null,
        string displayName = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, displayName ?? subject),
            new(TeamClaimTypes.TeamKey, memberOfTeam)
        };

        claims.AddRange((scopes ?? []).Select(s => new Claim(TeamClaimTypes.Scope, s)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

        return new AuthorizationSupportCaseServiceDecorator(
            new SupportCaseService(store ?? new InMemorySupportCaseStore(), authorizer, TimeProvider.System),
            authorizer);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
