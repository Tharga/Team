using System.Security.Claims;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Users;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>The mail sent to one user, newest first, of every kind: read by the user themselves, or by staff holding users:manage, which is audited, and refused to anyone else.</summary>
public class UserMailHistoryTests
{
    private const string Other = "user-2";

    private readonly UserMailFixture _mail = new();

    private UserMailHistoryService Service(string currentUserKey = UserMailFixture.UserKey, params Claim[] claims)
    {
        var users = Substitute.For<IUserService>();
        users.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(_ =>
        {
            if (currentUserKey == null) return Task.FromResult<IUser>(null);

            var user = Substitute.For<IUser>();
            user.Key.Returns(currentUserKey);
            return Task.FromResult(user);
        });

        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Sam"), .. claims], "Test"));

        return new UserMailHistoryService(
            _mail.Queue,
            _mail.Recipients,
            new TeamAuthorizer(new FixedPrincipal(principal)),
            users,
            new CompositeAuditLogger([_mail.Audit], Options.Create(new AuditOptions())),
            new AuditEntryFactory(null));
    }

    private static Claim Manage => new(TeamClaimTypes.SystemScope, SystemUserScopes.Manage);

    private Task<UserMailQueued> QueueAsync(string subject, UserMailKind kind = UserMailKind.Mail, string userKey = UserMailFixture.UserKey, string category = UserMailFixture.News)
        => _mail.Queue.QueueAsync(new UserMail { RecipientUserKey = userKey, Category = category, Subject = subject, Body = "Hello.", Kind = kind });

    private async Task<UserMailBatchQueued> MessageAsync(string subject, params string[] userKeys)
        => await _mail.Queue.QueueBatchAsync(new UserMailBatch { RecipientUserKeys = userKeys, Category = UserMailCategories.ServiceKey, Subject = subject, Body = "Planned maintenance." });

    [Fact]
    public async Task AUser_ReadsTheirOwnMail_NewestFirst_OfEveryKind()
    {
        await QueueAsync("A mail");
        _mail.Time.Advance(TimeSpan.FromMinutes(1));
        await MessageAsync("A message", UserMailFixture.UserKey, Other);
        _mail.Time.Advance(TimeSpan.FromMinutes(1));
        await QueueAsync("A notice", UserMailKind.Notice);

        var page = await Service().GetMineAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["A notice", "A message", "A mail"], page.Items.Select(x => x.Subject));
        Assert.Equal([UserMailKind.Notice, UserMailKind.Message, UserMailKind.Mail], page.Items.Select(x => x.Kind));
        Assert.Equal([UserMailFixture.News, UserMailCategories.ServiceKey, UserMailFixture.News], page.Items.Select(x => x.Category));
        Assert.Equal(UserMailFixture.Start.AddMinutes(2), page.Items[0].QueuedAt);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task EachMail_SaysWhereItStands_WhyAndWhen_AndTheAddressItWentTo()
    {
        await QueueAsync("Sent");
        await QueueAsync("Turned off", category: UserMailFixture.Digest);
        _mail.Time.Advance(TimeSpan.FromMinutes(4));
        await _mail.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);

        var page = await Service().GetMineAsync(cancellationToken: TestContext.Current.CancellationToken);

        var skipped = page.Items.Single(x => x.Subject == "Turned off");
        Assert.Equal(UserMailStatus.Skipped, skipped.Status);
        Assert.Equal(UserMailDispatcher.OptedOutReason, skipped.Reason);
        Assert.Equal(UserMailFixture.Start.AddMinutes(4), skipped.ChangedAt);

        var sent = page.Items.Single(x => x.Subject == "Sent");
        Assert.Equal(UserMailStatus.Sent, sent.Status);
        Assert.Equal(UserMailFixture.Address, sent.Address);
        Assert.Equal(UserMailFixture.Start, sent.QueuedAt);
        Assert.Equal(UserMailFixture.Start.AddMinutes(4), sent.ChangedAt);
    }

    [Fact]
    public async Task AnotherUsersMail_IsNotInMine()
    {
        await QueueAsync("Mine");
        await QueueAsync("Theirs", userKey: Other);
        await MessageAsync("Only theirs", Other);

        var page = await Service().GetMineAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["Mine"], page.Items.Select(x => x.Subject));
    }

    [Fact]
    public async Task TheHistory_IsReadAPageAtATime()
    {
        for (var i = 1; i <= 5; i++)
        {
            await QueueAsync($"Mail {i}");
            _mail.Time.Advance(TimeSpan.FromMinutes(1));
        }

        var service = Service();
        var first = await service.GetMineAsync(pageSize: 3, cancellationToken: TestContext.Current.CancellationToken);
        var second = await service.GetMineAsync(first.NextCursor, 3, TestContext.Current.CancellationToken);

        Assert.Equal(["Mail 5", "Mail 4", "Mail 3"], first.Items.Select(x => x.Subject));
        Assert.Equal(["Mail 2", "Mail 1"], second.Items.Select(x => x.Subject));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task NobodySignedIn_HasNoHistory()
    {
        await QueueAsync("A mail");

        var page = await Service(currentUserKey: null).GetMineAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task AUserNamingThemselves_ReadsWithoutAScope_AndIsNotAudited()
    {
        await QueueAsync("A mail");

        var page = await Service().GetForUserAsync(UserMailFixture.UserKey, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(page.Items);
        Assert.Empty(_mail.Audit.Entries);
    }

    [Fact]
    public async Task StaffWithUsersManage_ReadAnotherUsersHistory_AndThatIsAudited_WithoutAnAddress()
    {
        await QueueAsync("Theirs", userKey: Other);
        await MessageAsync("Also theirs", Other);

        var page = await Service("staff-1", Manage).GetForUserAsync(Other, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["Also theirs", "Theirs"], page.Items.Select(x => x.Subject));

        var entry = Assert.Single(_mail.Audit.Entries);
        Assert.Equal(AuditEventType.ServiceCall, entry.EventType);
        Assert.Equal(UserMailAudit.Feature, entry.Feature);
        Assert.Equal(UserMailAudit.HistoryAction, entry.Action);
        Assert.True(entry.Success);
        Assert.Equal(Other, entry.Metadata[UserMailAudit.UserKey]);
        Assert.Equal("2", entry.Metadata[UserMailAudit.Count]);
        Assert.DoesNotContain(entry.Metadata.Values, x => x.Contains('@'));
    }

    [Fact]
    public async Task AnotherUsersHistory_WithoutUsersManage_IsRefused_AndTheRefusalIsAudited()
    {
        await QueueAsync("Theirs", userKey: Other);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service("staff-1").GetForUserAsync(Other, cancellationToken: TestContext.Current.CancellationToken));

        var entry = Assert.Single(_mail.Audit.Entries);
        Assert.Equal(UserMailAudit.HistoryAction, entry.Action);
        Assert.False(entry.Success);
        Assert.Equal(Other, entry.Metadata[UserMailAudit.UserKey]);
        Assert.Contains(SystemUserScopes.Manage, entry.ErrorMessage);
    }

    [Fact]
    public async Task TheSameNameAsATeamScope_DoesNotOpenAnotherUsersHistory()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service("staff-1", new Claim(TeamClaimTypes.Scope, SystemUserScopes.Manage), new Claim(TeamClaimTypes.TeamKey, "team-1"))
            .GetForUserAsync(Other, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NoUserKey_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service("staff-1", Manage).GetForUserAsync(" ", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ANoticeIsQueuedAsANotice_AndAMessageAsAMessage()
    {
        var notice = await QueueAsync("A notice", UserMailKind.Notice);
        var message = await MessageAsync("A message", UserMailFixture.UserKey);

        Assert.Equal(UserMailKind.Notice, (await _mail.Queue.GetAsync(notice.Id)).Kind);
        Assert.Equal(UserMailKind.Message, _mail.Outbox.Items.Single(x => x.BatchId == message.Id).Mail.Kind);
    }

    private sealed class FixedPrincipal(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
