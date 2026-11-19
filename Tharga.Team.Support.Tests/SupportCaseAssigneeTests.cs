using System.Security.Claims;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// A support case is assigned to one member of the product's own staff, or to nobody: who may do it, who may be chosen, and what
/// is left behind in the transcript, the live notification and the audit log.
/// </summary>
/// <remarks>
/// <b>The refusals are the tests that earn their place.</b> The staff are the product's, never the customer's, so every negative
/// here is asserted against a caller who is fully privileged <i>in the case's own team</i> and still gets nothing.
/// </remarks>
public class SupportCaseAssigneeTests
{
    private const string TeamA = "team-a";
    private const string Olivia = "olivia-subject";
    private const string Sam = "sam-subject";

    private static readonly SupportCaseAssignee SamAsStaff = new() { Key = Sam, Name = "Sam Support", Email = "sam@example.com" };

    [Fact]
    public async Task ACaseInATeam_IsAssignedByWhoeverMayAnswerAcrossTeams()
    {
        var world = new World();
        var caseId = await world.TeamCaseAsync();

        await world.As(Olivia, systemScopes: [SystemSupportScopes.AllManage]).SetCaseAssigneeAsync(TeamA, caseId, Olivia);

        Assert.Equal(Olivia, (await world.CaseAsync(TeamA, caseId)).Assignee.Key);
    }

    [Fact]
    public async Task AnUnassignedCase_IsAssignedByWhoeverWorksTheQueue()
    {
        var world = new World();
        var caseId = await world.UnassignedCaseAsync();

        await world.As(Olivia, systemScopes: [SystemSupportScopes.Manage]).SetCaseAssigneeAsync(null, caseId, Olivia);

        Assert.Equal(Olivia, (await world.CaseAsync(null, caseId)).Assignee.Key);
    }

    [Fact]
    public async Task AMemberWithEveryTeamScope_CannotAssignAStaffMember()
    {
        var world = new World();
        var caseId = await world.TeamCaseAsync();
        var member = world.As(Olivia, teamScopes: [SupportScopes.Read, SupportScopes.Manage]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => member.SetCaseAssigneeAsync(TeamA, caseId, Olivia));

        Assert.Null((await world.CaseAsync(TeamA, caseId)).Assignee);
    }

    [Theory]
    [InlineData(SystemSupportScopes.Read)]
    [InlineData(SystemSupportScopes.AllRead)]
    public async Task ReadingIsNotPermissionToAssign(string scope)
    {
        var world = new World();
        var teamCase = await world.TeamCaseAsync();
        var unassigned = await world.UnassignedCaseAsync();
        var reader = world.As(Olivia, systemScopes: [scope]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.SetCaseAssigneeAsync(TeamA, teamCase, Olivia));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.SetCaseAssigneeAsync(null, unassigned, Olivia));
    }

    /// <summary>Holding one queue must never confer the other, as it never has for answering.</summary>
    [Fact]
    public async Task EachGrant_CoversOnlyItsOwnQueue()
    {
        var world = new World();
        var teamCase = await world.TeamCaseAsync();
        var unassigned = await world.UnassignedCaseAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => world.As(Olivia, systemScopes: [SystemSupportScopes.Manage]).SetCaseAssigneeAsync(TeamA, teamCase, Olivia));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => world.As(Olivia, systemScopes: [SystemSupportScopes.AllManage]).SetCaseAssigneeAsync(null, unassigned, Olivia));
    }

    [Fact]
    public async Task ACaller_CanAssignTheCaseToThemselves_WithNoDirectoryRegistered()
    {
        var world = new World();
        var caseId = await world.TeamCaseAsync();

        await world.Staff(Olivia, "Olivia Operator").SetCaseAssigneeAsync(TeamA, caseId, Olivia);

        var assignee = (await world.CaseAsync(TeamA, caseId)).Assignee;
        Assert.Equal(Olivia, assignee.Key);
        Assert.Equal("Olivia Operator", assignee.Name);
    }

    [Fact]
    public async Task WithNoDirectory_NobodyElseCanBeChosen()
    {
        var world = new World();
        var caseId = await world.TeamCaseAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => world.Staff(Olivia).SetCaseAssigneeAsync(TeamA, caseId, Sam));
    }

    [Fact]
    public async Task APersonTheDirectoryLists_CanBeChosen_WithTheirNameAndAddress()
    {
        var world = new World(directory: [SamAsStaff]);
        var caseId = await world.TeamCaseAsync();

        await world.Staff(Olivia).SetCaseAssigneeAsync(TeamA, caseId, Sam);

        Assert.Equal(SamAsStaff, (await world.CaseAsync(TeamA, caseId)).Assignee);
    }

    /// <summary>The service refuses what the picker would never offer, so a hand-made call gains nothing.</summary>
    [Fact]
    public async Task APersonTheDirectoryDoesNotList_IsRefused()
    {
        var world = new World(directory: [SamAsStaff]);
        var caseId = await world.TeamCaseAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => world.Staff(Olivia).SetCaseAssigneeAsync(TeamA, caseId, "a-customer-subject"));

        Assert.Null((await world.CaseAsync(TeamA, caseId)).Assignee);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ABlankKey_TakesTheAssigneeAway(string blank)
    {
        var world = new World();
        var caseId = await world.TeamCaseAsync();
        var staff = world.Staff(Olivia);
        await staff.SetCaseAssigneeAsync(TeamA, caseId, Olivia);

        await staff.SetCaseAssigneeAsync(TeamA, caseId, blank);

        Assert.Null((await world.CaseAsync(TeamA, caseId)).Assignee);
    }

    /// <summary>The customer's own team reads the transcript, and who staffs their case is not for them.</summary>
    [Fact]
    public async Task AssigningACase_AddsNothingToTheTranscriptTheCustomerReads()
    {
        var world = new World(directory: [SamAsStaff]);
        var caseId = await world.TeamCaseAsync();
        var staff = world.Staff(Olivia, "Olivia Operator");
        var before = await world.MessagesAsync(TeamA, caseId);

        await staff.SetCaseAssigneeAsync(TeamA, caseId, Sam);
        await staff.SetCaseAssigneeAsync(TeamA, caseId, null);

        Assert.Equal(before.Length, (await world.MessagesAsync(TeamA, caseId)).Length);
        Assert.Equal(before.Length, (await world.CaseAsync(TeamA, caseId)).MessageCount);
    }

    [Fact]
    public async Task NamingThePersonAlreadyAssigned_ChangesNothingAndSaysNothing()
    {
        var world = new World();
        var caseId = await world.TeamCaseAsync();
        var staff = world.Staff(Olivia);
        await staff.SetCaseAssigneeAsync(TeamA, caseId, Olivia);
        var seen = 0;
        world.Notifier.CaseUpdated += (_, _) => seen++;

        await staff.SetCaseAssigneeAsync(TeamA, caseId, Olivia);

        Assert.Equal(0, seen);
        Assert.Equal(Olivia, (await world.CaseAsync(TeamA, caseId)).Assignee.Key);
    }

    [Fact]
    public async Task ClearingACaseThatHasNoAssignee_ChangesNothingAndSaysNothing()
    {
        var world = new World();
        var caseId = await world.TeamCaseAsync();
        var seen = 0;
        world.Notifier.CaseUpdated += (_, _) => seen++;

        await world.Staff(Olivia).SetCaseAssigneeAsync(TeamA, caseId, null);

        Assert.Equal(0, seen);
    }

    [Fact]
    public async Task AClosedCase_CanBeAssigned_AndClosingLeavesTheAssignee()
    {
        var world = new World();
        var caseId = await world.TeamCaseAsync();
        var staff = world.Staff(Olivia);

        await staff.CloseCaseAsync(TeamA, caseId);
        await staff.SetCaseAssigneeAsync(TeamA, caseId, Olivia);
        await staff.ReopenCaseAsync(TeamA, caseId);

        Assert.Equal(Olivia, (await world.CaseAsync(TeamA, caseId)).Assignee.Key);
        await staff.CloseCaseAsync(TeamA, caseId);
        Assert.Equal(Olivia, (await world.CaseAsync(TeamA, caseId)).Assignee.Key);
    }

    /// <summary>Which tenant a case belongs to and who works it are separate facts.</summary>
    [Fact]
    public async Task GivingACaseATeam_LeavesItsAssignee()
    {
        var world = new World();
        var caseId = await world.UnassignedCaseAsync();
        await world.As(Olivia, systemScopes: [SystemSupportScopes.Manage], name: "Olivia Operator").SetCaseAssigneeAsync(null, caseId, Olivia);

        await world.As(Olivia, systemScopes: [SystemSupportScopes.Manage]).AssignCaseAsync(caseId, TeamA);

        var assigned = await world.CaseAsync(TeamA, caseId);
        Assert.Equal(TeamA, assigned.TeamKey);
        Assert.Equal(Olivia, assigned.Assignee.Key);
    }

    [Fact]
    public async Task AnAssignment_IsAnnouncedToEveryScreenOpenOnTheCase()
    {
        var world = new World();
        var caseId = await world.TeamCaseAsync();
        SupportCaseUpdatedEventArgs seen = null;
        world.Notifier.CaseUpdated += (_, e) => seen = e;

        await world.Staff(Olivia).SetCaseAssigneeAsync(TeamA, caseId, Olivia);

        Assert.Equal(SupportCaseChange.AssigneeChanged, seen.Change);
        Assert.Equal(TeamA, seen.TeamKey);
        Assert.Equal(caseId, seen.CaseId);
    }

    [Fact]
    public async Task AnAssignment_IsAnnouncedForAnUnassignedCaseWithNoTeam()
    {
        var world = new World();
        var caseId = await world.UnassignedCaseAsync();
        SupportCaseUpdatedEventArgs seen = null;
        world.Notifier.CaseUpdated += (_, e) => seen = e;

        await world.As(Olivia, systemScopes: [SystemSupportScopes.Manage]).SetCaseAssigneeAsync(null, caseId, Olivia);

        Assert.Null(seen.TeamKey);
        Assert.Equal(SupportCaseChange.AssigneeChanged, seen.Change);
    }

    [Fact]
    public async Task AnAssignment_IsAudited_WithTheCaseAndTheNewAssignee()
    {
        var world = new World();
        var caseId = await world.TeamCaseAsync();

        await world.Staff(Olivia).SetCaseAssigneeAsync(TeamA, caseId, Olivia);

        var entry = world.Audit.Entries.Single(x => x.Action == "assign-to-staff");
        Assert.True(entry.Success);
        Assert.Equal(caseId, entry.Metadata[SupportAuditMetadataKeys.CaseId]);
        Assert.Equal(Olivia, entry.Metadata[SupportAuditMetadataKeys.AssigneeKey]);
    }

    [Fact]
    public async Task TakingTheAssigneeAway_IsAuditedWithNoAssignee()
    {
        var world = new World();
        var caseId = await world.TeamCaseAsync();
        var staff = world.Staff(Olivia);
        await staff.SetCaseAssigneeAsync(TeamA, caseId, Olivia);

        await staff.SetCaseAssigneeAsync(TeamA, caseId, null);

        var entry = world.Audit.Entries.Last(x => x.Action == "assign-to-staff");
        Assert.Equal(string.Empty, entry.Metadata[SupportAuditMetadataKeys.AssigneeKey]);
    }

    [Fact]
    public async Task ARefusedAssignment_IsAuditedAsAFailure()
    {
        var world = new World();
        var caseId = await world.TeamCaseAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => world.As(Olivia, teamScopes: [SupportScopes.Manage]).SetCaseAssigneeAsync(TeamA, caseId, Olivia));

        Assert.False(world.Audit.Entries.Single(x => x.Action == "assign-to-staff").Success);
    }

    [Fact]
    public async Task TheStaffOffered_AreTheDirectorysAndTheCaller_OrderedByName_WithNobodyTwice()
    {
        var zed = new SupportCaseAssignee { Key = "zed-subject", Name = "Zed Zimmerman" };
        var world = new World(directory: [zed, SamAsStaff, new SupportCaseAssignee { Key = Olivia, Name = "Olivia (directory)" }]);

        var offered = await world.Staff(Olivia, "Olivia Operator").GetAssignableStaffAsync();

        Assert.Equal([Olivia, Sam, "zed-subject"], offered.Select(x => x.Key).ToArray());
    }

    [Fact]
    public async Task WithNoDirectory_OnlyTheCallerIsOffered()
    {
        var world = new World();

        var offered = await world.Staff(Olivia, "Olivia Operator").GetAssignableStaffAsync();

        var only = Assert.Single(offered);
        Assert.Equal((Olivia, "Olivia Operator"), (only.Key, only.Name));
    }

    [Fact]
    public async Task OnlyStaffAreToldWhoCanBeAssigned()
    {
        var world = new World(directory: [SamAsStaff]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => world.As(Olivia, teamScopes: [SupportScopes.Read, SupportScopes.Manage]).GetAssignableStaffAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => world.As(Olivia, systemScopes: [SystemSupportScopes.AllRead]).GetAssignableStaffAsync());

        Assert.NotEmpty(await world.As(Olivia, systemScopes: [SystemSupportScopes.Manage]).GetAssignableStaffAsync());
        Assert.NotEmpty(await world.As(Olivia, systemScopes: [SystemSupportScopes.AllManage]).GetAssignableStaffAsync());
    }

    [Fact]
    public async Task AnUnknownCase_IsReportedAsNotFound()
    {
        var world = new World();

        await Assert.ThrowsAsync<InvalidOperationException>(() => world.Staff(Olivia).SetCaseAssigneeAsync(TeamA, "no-such-case", Olivia));
    }

    private sealed class World(SupportCaseAssignee[] directory = null)
    {
        private readonly InMemorySupportCaseStore _store = new();
        private readonly ISupportStaffDirectory _directory = directory == null ? null : new FixedDirectory(directory);

        public SupportCaseNotifier Notifier { get; } = new();

        public CollectingAuditLogger Audit { get; } = new();

        public async Task<string> TeamCaseAsync()
        {
            await _store.AddCaseAsync(NewCase("team-case", TeamA), Arrived());
            return "team-case";
        }

        public async Task<string> UnassignedCaseAsync()
        {
            await _store.AddCaseAsync(NewCase("unassigned-case", null), Arrived());
            return "unassigned-case";
        }

        public Task<SupportCase> CaseAsync(string teamKey, string caseId) => _store.GetCaseAsync(teamKey, caseId);

        public async Task<SupportMessage[]> MessagesAsync(string teamKey, string caseId) => [.. (await _store.GetMessagesAsync(teamKey, caseId, null, 100)).Items];

        public ISupportCaseService Staff(string subject, string name = null)
            => As(subject, systemScopes: [SystemSupportScopes.Manage, SystemSupportScopes.AllManage], name: name);

        public ISupportCaseService As(string subject, string team = TeamA, string[] teamScopes = null, string[] systemScopes = null, string name = null)
        {
            List<Claim> claims = [new(ClaimTypes.NameIdentifier, subject), new(ClaimTypes.Name, name ?? subject)];
            if (team != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, team));
            claims.AddRange((teamScopes ?? []).Select(x => new Claim(TeamClaimTypes.Scope, x)));
            claims.AddRange((systemScopes ?? []).Select(x => new Claim(TeamClaimTypes.SystemScope, x)));
            var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));

            return new AuditingSupportCaseServiceDecorator(
                new AuthorizationSupportCaseServiceDecorator(
                    new SupportCaseService(_store, authorizer, TimeProvider.System, notifier: Notifier, staffDirectory: _directory), authorizer),
                new CompositeAuditLogger([Audit], Options.Create(new AuditOptions())),
                new AuditEntryFactory(null));
        }

        private static SupportCase NewCase(string id, string teamKey) => new()
        {
            Id = id,
            TeamKey = teamKey,
            AuthorIdentity = "customer-subject",
            AuthorName = "Cora Customer",
            Subject = "Cannot sign in",
            Status = SupportCaseStatus.Open,
            CreatedAt = DateTime.UtcNow,
            MessageCount = 1
        };

        private static SupportMessage Arrived() => new()
        {
            Sequence = 1,
            Kind = SupportMessageKind.User,
            AuthorIdentity = "customer-subject",
            AuthorName = "Cora Customer",
            Body = "It says my key expired.",
            SentAt = DateTime.UtcNow
        };
    }

    private sealed class FixedDirectory(SupportCaseAssignee[] staff) : ISupportStaffDirectory
    {
        public Task<SupportCaseAssignee[]> GetStaffAsync(CancellationToken cancellationToken = default) => Task.FromResult(staff);
    }

    private sealed class CollectingAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Log(AuditEntry entry) => Entries.Add(entry);

        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
