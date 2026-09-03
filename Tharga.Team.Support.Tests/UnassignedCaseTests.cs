using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// A case that belongs to no team: who may see it, who may give it one, and what the transcript says
/// afterwards.
/// </summary>
/// <remarks>
/// <b>The refusals are the tests that earn their place.</b> An unassigned case may concern any tenant or
/// none, so the failure this file exists to prevent is a team scope quietly reaching one — a member of the
/// smallest team reading everything that arrived by mail. Every negative here is asserted against a caller
/// who is fully privileged <i>in their own team</i> and still gets nothing.
/// </remarks>
public class UnassignedCaseTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";

    [Fact]
    public async Task ListingTheUnassignedQueue_RequiresTheSystemReadScope()
    {
        var store = await StoreWithOneUnassignedCase();

        var privilegedInTheirOwnTeam = Build(store, teamScopes: [SupportScopes.Read, SupportScopes.Manage]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => privilegedInTheirOwnTeam.GetUnassignedCasesAsync());

        var operatorService = Build(store, systemScopes: [SystemSupportScopes.Read]);

        Assert.Single((await operatorService.GetUnassignedCasesAsync()).Items);
    }

    [Fact]
    public async Task TheUnassignedQueue_HoldsOnlyCasesWithNoTeam()
    {
        var store = await StoreWithOneUnassignedCase();
        await Build(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        var page = await Build(store, systemScopes: [SystemSupportScopes.Read]).GetUnassignedCasesAsync();

        Assert.Single(page.Items);
        Assert.All(page.Items, x => Assert.True(string.IsNullOrEmpty(x.TeamKey)));
    }

    [Fact]
    public async Task ReadingAnUnassignedCase_RequiresTheSystemReadScope()
    {
        var store = await StoreWithOneUnassignedCase();
        var caseId = await UnassignedCaseId(store);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Build(store, teamScopes: [SupportScopes.Read]).GetCaseAsync(null, caseId));

        var read = await Build(store, systemScopes: [SystemSupportScopes.Read]).GetCaseAsync(null, caseId);

        Assert.Equal(caseId, read.Id);
    }

    [Fact]
    public async Task AssigningACase_RequiresTheSystemManageScope()
    {
        var store = await StoreWithOneUnassignedCase();
        var caseId = await UnassignedCaseId(store);

        // Reading the queue is not permission to move a case out of it.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Build(store, systemScopes: [SystemSupportScopes.Read]).AssignCaseAsync(caseId, TeamA));

        Assert.True(await Build(store, systemScopes: [SystemSupportScopes.Manage]).AssignCaseAsync(caseId, TeamA));
    }

    /// <summary>
    /// After assignment the case is the team's: readable by its members, and gone from the queue.
    /// </summary>
    [Fact]
    public async Task AnAssignedCase_BelongsToTheTeamAndLeavesTheQueue()
    {
        var store = await StoreWithOneUnassignedCase();
        var caseId = await UnassignedCaseId(store);

        await Build(store, systemScopes: [SystemSupportScopes.Manage]).AssignCaseAsync(caseId, TeamA);

        var read = await Build(store, teamScopes: [SupportScopes.Read]).GetCaseAsync(TeamA, caseId);
        Assert.Equal(TeamA, read.TeamKey);

        Assert.Empty((await Build(store, systemScopes: [SystemSupportScopes.Read]).GetUnassignedCasesAsync()).Items);
    }

    /// <summary>
    /// Which tenant a case belongs to is part of its history — one that changed hands and says nothing about
    /// it reads as having always been there.
    /// </summary>
    [Fact]
    public async Task AssigningACase_RecordsItInTheTranscript()
    {
        var store = await StoreWithOneUnassignedCase();
        var caseId = await UnassignedCaseId(store);

        await Build(store, systemScopes: [SystemSupportScopes.Manage], displayName: "Olivia Operator")
            .AssignCaseAsync(caseId, TeamA);

        var messages = await Build(store, teamScopes: [SupportScopes.Read]).GetMessagesAsync(TeamA, caseId);

        var note = messages.Items[^1];
        Assert.Equal(SupportMessageKind.System, note.Kind);
        Assert.Contains(TeamA, note.Body);
        Assert.Contains("Olivia Operator", note.Body);
    }

    /// <summary>
    /// The second of two agents triaging the same queue is told it changed nothing, rather than moving a case
    /// that already belongs to somebody.
    /// </summary>
    [Fact]
    public async Task AssigningACaseThatAlreadyHasATeam_ChangesNothingAndSaysSo()
    {
        var store = await StoreWithOneUnassignedCase();
        var caseId = await UnassignedCaseId(store);

        var operatorService = Build(store, systemScopes: [SystemSupportScopes.Manage]);

        Assert.True(await operatorService.AssignCaseAsync(caseId, TeamA));
        Assert.False(await operatorService.AssignCaseAsync(caseId, "team-b"));

        Assert.Equal(TeamA, (await Build(store, teamScopes: [SupportScopes.Read]).GetCaseAsync(TeamA, caseId)).TeamKey);
    }

    [Fact]
    public async Task AssigningToNoTeam_IsRefused()
    {
        var store = await StoreWithOneUnassignedCase();
        var caseId = await UnassignedCaseId(store);

        await Assert.ThrowsAsync<ArgumentException>(
            () => Build(store, systemScopes: [SystemSupportScopes.Manage]).AssignCaseAsync(caseId, null));
    }

    /// <summary>
    /// Answering an unassigned case takes the managing scope: it is a write on a case whose tenant nobody has
    /// established yet.
    /// </summary>
    [Fact]
    public async Task ReplyingToAnUnassignedCase_TakesTheSystemManageScope()
    {
        var store = await StoreWithOneUnassignedCase();
        var caseId = await UnassignedCaseId(store);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Build(store, systemScopes: [SystemSupportScopes.Read]).ReplyToCaseAsync(null, caseId, "Looking into it."));

        await Build(store, systemScopes: [SystemSupportScopes.Manage]).ReplyToCaseAsync(null, caseId, "Looking into it.");

        var messages = await Build(store, systemScopes: [SystemSupportScopes.Read]).GetMessagesAsync(null, caseId);
        Assert.Equal("Looking into it.", messages.Items[^1].Body);
    }

    /// <summary>
    /// A caller with no subject claim must be shown nothing, not every unattributed case.
    /// </summary>
    /// <remarks>
    /// A case raised by inbound mail has no author identity, so "my cases" matching on an empty subject
    /// would list all of them as the caller's own. A principal without a name identifier is a configuration
    /// a host can produce, so this is a guard rather than a hypothetical.
    /// </remarks>
    [Fact]
    public async Task MyCases_MatchesNothingWhenTheCallerHasNoSubject()
    {
        var store = new InMemorySupportCaseStore();

        // An unattributed case that has since been assigned to the team the caller is in.
        await store.AddCaseAsync(
            new SupportCase
            {
                Id = "case-from-mail",
                TeamKey = TeamA,
                AuthorIdentity = null,
                AuthorName = "stranger@example.com",
                Subject = "Cannot sign in",
                Status = SupportCaseStatus.Open,
                CreatedAt = DateTime.UtcNow,
                MessageCount = 1
            },
            new SupportMessage
            {
                Sequence = 1,
                Kind = SupportMessageKind.User,
                AuthorName = "stranger@example.com",
                Body = "It says my key expired.",
                SentAt = DateTime.UtcNow
            });

        var anonymous = BuildWithoutSubject(store);

        Assert.Empty((await anonymous.GetMyCasesAsync(TeamA)).Items);
    }

    /// <summary>
    /// A case arriving with no team, as inbound mail from a sender whose team could not be determined does.
    /// Written straight to the store because no operation raises one yet — step E adds it.
    /// </summary>
    private static async Task<InMemorySupportCaseStore> StoreWithOneUnassignedCase()
    {
        var store = new InMemorySupportCaseStore();

        var supportCase = new SupportCase
        {
            Id = "case-unassigned",
            TeamKey = null,
            // Unattributed, as mail from a stranger is: there is no account to point at, only an address.
            AuthorIdentity = null,
            Subject = "Cannot sign in",
            AuthorName = "stranger@example.com",
            Status = SupportCaseStatus.Open,
            CreatedAt = DateTime.UtcNow,
            MessageCount = 1
        };

        await store.AddCaseAsync(supportCase, new SupportMessage
        {
            Sequence = 1,
            Kind = SupportMessageKind.User,
            AuthorName = "stranger@example.com",
            Body = "It says my key expired.",
            SentAt = DateTime.UtcNow,
            Source = SupportChannelType.Email
        });

        return store;
    }

    private static async Task<string> UnassignedCaseId(InMemorySupportCaseStore store)
        => (await store.GetUnassignedCasesAsync(null, 20)).Items[0].Id;

    /// <summary>A signed-in caller whose principal carries no name identifier.</summary>
    private static ISupportCaseService BuildWithoutSubject(InMemorySupportCaseStore store)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(TeamClaimTypes.TeamKey, TeamA)], "test"));
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

        return new AuthorizationSupportCaseServiceDecorator(
            new SupportCaseService(store, authorizer, TimeProvider.System),
            authorizer);
    }

    private static ISupportCaseService Build(
        InMemorySupportCaseStore store,
        string[] teamScopes = null,
        string[] systemScopes = null,
        string displayName = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Alice),
            new(ClaimTypes.Name, displayName ?? Alice),
            new(TeamClaimTypes.TeamKey, TeamA)
        };

        claims.AddRange((teamScopes ?? []).Select(s => new Claim(TeamClaimTypes.Scope, s)));
        claims.AddRange((systemScopes ?? []).Select(s => new Claim(TeamClaimTypes.SystemScope, s)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

        return new AuthorizationSupportCaseServiceDecorator(
            new SupportCaseService(store, authorizer, TimeProvider.System),
            authorizer);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
