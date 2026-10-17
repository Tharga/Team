using Microsoft.AspNetCore.Http;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

public class AuditingTeamServiceDecoratorTests
{
    private readonly TestTeamService _inner;
    private readonly FakeAuditBackend _backend;
    private readonly AuditingTeamServiceDecorator _sut;

    public AuditingTeamServiceDecoratorTests()
    {
        var userService = Substitute.For<IUserService>();
        var user = Substitute.For<IUser>();
        user.Key.Returns("user-1");
        user.EMail.Returns("test@example.com");
        userService.GetCurrentUserAsync().Returns(user);

        _inner = new TestTeamService(userService);
        _inner.AddTeam("team-1", "Test Team",
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.User, State = MembershipState.Member });

        var (logger, backend) = FakeAuditLoggerFactory.Create();
        _backend = backend;
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _sut = new AuditingTeamServiceDecorator(_inner, logger, httpContextAccessor);
    }

    [Fact]
    public async Task CreateTeamAsync_LogsAuditEntry()
    {
        await _sut.CreateTeamAsync("New Team");

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("team", entry.Feature);
        Assert.Equal("create", entry.Action);
        Assert.True(entry.Success);
    }

    [Fact]
    public async Task RenameTeamAsync_LogsAuditEntry()
    {
        await _sut.RenameTeamAsync<TestMember>("team-1", "Renamed");

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("team", entry.Feature);
        Assert.Equal("rename", entry.Action);
        Assert.Equal("team-1", entry.TeamKey);
    }

    [Fact]
    public async Task DeleteTeamAsync_LogsAuditEntry()
    {
        await _sut.DeleteTeamAsync<TestMember>("team-1");

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("delete", entry.Action);
        Assert.Equal("team-1", entry.TeamKey);
    }

    [Fact]
    public async Task AddMemberAsync_LogsAuditEntry()
    {
        await _sut.AddMemberAsync("team-1", new InviteUserModel { Email = "new@test.com", AccessLevel = AccessLevel.User });

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("invite", entry.Action);
        Assert.Equal("team-1", entry.TeamKey);
    }

    [Fact]
    public async Task RemoveMemberAsync_LogsAuditEntry()
    {
        await _sut.RemoveMemberAsync("team-1", "user-2");

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("remove-member", entry.Action);
        Assert.Equal("team-1", entry.TeamKey);
    }

    [Fact]
    public async Task SetMemberRoleAsync_LogsAuditEntry()
    {
        await _sut.SetMemberRoleAsync("team-1", "user-2", AccessLevel.Administrator);

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("set-role", entry.Action);
    }

    [Fact]
    public async Task SetMemberTenantRolesAsync_LogsAuditEntry()
    {
        await _sut.SetMemberTenantRolesAsync("team-1", "user-2", new[] { "Editor" });

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("set-tenant-roles", entry.Action);
    }

    [Fact]
    public async Task SetMemberScopeOverridesAsync_LogsAuditEntry()
    {
        await _sut.SetMemberScopeOverridesAsync("team-1", "user-2", new[] { "feature:read" });

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("set-scope-overrides", entry.Action);
    }

    [Fact]
    public async Task SetMemberNameAsync_LogsAuditEntry()
    {
        await _sut.SetMemberNameAsync("team-1", "user-2", "New Name");

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("team", entry.Feature);
        Assert.Equal("set-member-name", entry.Action);
        Assert.Equal("team-1", entry.TeamKey);
        Assert.True(entry.Success);
    }

    [Fact]
    public async Task SetInvitationResponseAsync_Accept_LogsAuditEntry()
    {
        await _sut.SetInvitationResponseAsync("team-1", "user-2", "invite-key", true);

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("accept-invite", entry.Action);
    }

    [Fact]
    public async Task SetInvitationResponseAsync_Reject_LogsAuditEntry()
    {
        await _sut.SetInvitationResponseAsync("team-1", "user-2", "invite-key", false);

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("reject-invite", entry.Action);
    }

    [Fact]
    public async Task SetTeamConsentAsync_LogsAuditEntry()
    {
        await _sut.SetTeamConsentAsync("team-1", new[] { "Developer" });

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("set-consent", entry.Action);
    }

    [Fact]
    public async Task SetTeamCustomRolesAsync_LogsAuditEntry()
    {
        await _sut.SetTeamCustomRolesAsync("team-1", [new TenantRoleDefinition("Registrar", ["case:read"])]);

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("team", entry.Feature);
        Assert.Equal("set-custom-roles", entry.Action);
        Assert.Equal("team-1", entry.TeamKey);
        Assert.True(entry.Success);
    }

    [Fact]
    public async Task TransferOwnershipAsync_LogsAuditEntry()
    {
        await _sut.TransferOwnershipAsync<TestMember>("team-1", "user-2");

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("transfer-ownership", entry.Action);
    }

    [Fact]
    public async Task ReadOperations_DoNotLog()
    {
        await _sut.GetTeamsAsync().ToArrayAsync(TestContext.Current.CancellationToken);
        await _sut.GetTeamsAsync<TestMember>().ToArrayAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_backend.Entries);
    }

    [Fact]
    public async Task FailedOperation_LogsWithErrorMessage()
    {
        // Owner cannot leave — should log failure
        try { await _sut.RemoveMemberAsync("team-1", "user-1"); } catch { }

        var entry = Assert.Single(_backend.Entries);
        Assert.False(entry.Success);
        Assert.NotNull(entry.ErrorMessage);
    }

    [Fact]
    public async Task AllEntries_HaveServiceCallEventType()
    {
        await _sut.CreateTeamAsync("Test");
        await _sut.AddMemberAsync("team-1", new InviteUserModel { Email = "x@x.com", AccessLevel = AccessLevel.User });

        Assert.All(_backend.Entries, e => Assert.Equal(AuditEventType.ServiceCall, e.EventType));
    }
}
