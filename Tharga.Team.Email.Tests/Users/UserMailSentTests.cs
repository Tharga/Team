using System.Security.Claims;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Users;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>
/// One logged mail, read by the user who sent it or by staff holding users:manage, with the headers it went with, who sent
/// it, the case it belongs to and why it did not arrive; anyone else, and an id nobody knows, gets nothing.
/// </summary>
public class UserMailSentTests
{
    private const string Sender = "staff-1";
    private const string Stranger = "user-9";

    private readonly UserMailFixture _mail = new();

    private UserMailHistoryService Service(string currentUserKey, params Claim[] claims)
    {
        var users = Substitute.For<IUserService>();
        users.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(_ =>
        {
            if (currentUserKey == null) return Task.FromResult<IUser>(null);

            var user = Substitute.For<IUser>();
            user.Key.Returns(currentUserKey);
            return Task.FromResult(user);
        });

        var principal = currentUserKey == null && claims.Length == 0
            ? new ClaimsPrincipal(new ClaimsIdentity())
            : new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Sam"), .. claims], "Test"));

        return new UserMailHistoryService(
            _mail.Queue,
            _mail.Recipients,
            new TeamAuthorizer(new FixedPrincipal(principal)),
            users,
            new CompositeAuditLogger([_mail.Audit], Options.Create(new AuditOptions())),
            new AuditEntryFactory(null));
    }

    private static Claim Manage => new(TeamClaimTypes.SystemScope, SystemUserScopes.Manage);

    private Task<string> FailedAnswerAsync()
        => _mail.Outbox.RecordAsync(new UserMailRecord
        {
            Kind = UserMailKind.SupportAnswer,
            RecipientAddress = "kund@customer.example",
            RecipientName = "Kim Kund",
            Subject = "Re: Export is empty",
            TeamKey = "acme",
            SentByUserKey = Sender,
            SentByName = "Sam Staff",
            Headers = new UserMailHeaders("support@example.net", "Example Support", "case-1@example.net"),
            Status = UserMailStatus.Failed,
            Reason = "550 5.7.1 Relaying denied",
            At = UserMailFixture.Start,
            CaseId = "case-1",
            CaseMessageSequence = 2
        });

    [Fact]
    public async Task TheSender_ReadsTheMail_WithItsHeaders_Sender_CaseAndReason()
    {
        var id = await FailedAnswerAsync();

        var item = await Service(Sender).GetSentAsync(id, TestContext.Current.CancellationToken);

        Assert.NotNull(item);
        Assert.Equal((id, UserMailKind.SupportAnswer, UserMailStatus.Failed, "550 5.7.1 Relaying denied"), (item.Id, item.Kind, item.Status, item.Reason));
        Assert.Equal(("support@example.net", "Example Support", "case-1@example.net"), (item.FromAddress, item.FromName, item.ReplyTo));
        Assert.Equal((Sender, "Sam Staff"), (item.SentByUserKey, item.SentByName));
        Assert.Equal(("case-1", (int?)2, "acme"), (item.CaseId, item.CaseMessageSequence, item.TeamKey));
        Assert.Equal(("kund@customer.example", "Kim Kund", "Re: Export is empty"), (item.Address, item.RecipientName, item.Subject));
    }

    [Fact]
    public async Task TheSendersOwnRead_IsNotAudited()
    {
        var id = await FailedAnswerAsync();

        await Service(Sender).GetSentAsync(id, TestContext.Current.CancellationToken);

        Assert.Empty(_mail.Audit.Entries);
    }

    [Fact]
    public async Task StaffHoldingUsersManage_ReadAnyonesMail_AndTheReadIsAudited()
    {
        var id = await FailedAnswerAsync();

        var item = await Service(Stranger, Manage).GetSentAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal(id, item?.Id);
        var entry = Assert.Single(_mail.Audit.Entries);
        Assert.Equal((UserMailAudit.Feature, UserMailAudit.LogAction, true), (entry.Feature, entry.Action, entry.Success));
        Assert.Equal("1", entry.Metadata[UserMailAudit.Count]);
        Assert.DoesNotContain(entry.Metadata.Values, x => x.Contains("kund", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnotherSignedInUser_GetsNothing()
    {
        var id = await FailedAnswerAsync();

        Assert.Null(await Service(Stranger).GetSentAsync(id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheRecipient_GetsNothingHere_SinceTheMailIsNotTheirsToRead()
    {
        await _mail.Queue.QueueAsync(new UserMail { RecipientUserKey = UserMailFixture.UserKey, Category = UserMailFixture.News, Subject = "Hello", Body = "Hi.", SentByUserKey = Sender });
        var id = _mail.Outbox.Single.Id;

        Assert.Null(await Service(UserMailFixture.UserKey).GetSentAsync(id, TestContext.Current.CancellationToken));
        Assert.NotNull(await Service(Sender).GetSentAsync(id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MailTheApplicationSentOnItsOwn_IsNobodysToReadButStaffs()
    {
        await _mail.QueueAsync();
        var id = _mail.Outbox.Single.Id;

        Assert.Null(await Service(UserMailFixture.UserKey).GetSentAsync(id, TestContext.Current.CancellationToken));
        Assert.NotNull(await Service(Stranger, Manage).GetSentAsync(id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SomeoneSignedOut_GetsNothing()
    {
        var id = await FailedAnswerAsync();

        Assert.Null(await Service(null).GetSentAsync(id, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("no-such-mail")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task AnIdNobodyKnows_GivesNothing_EvenToStaff(string id)
    {
        await FailedAnswerAsync();

        Assert.Null(await Service(Stranger, Manage).GetSentAsync(id, TestContext.Current.CancellationToken));
        Assert.Null(await Service(Sender).GetSentAsync(id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AHostsOwnHistoryService_WithoutTheRead_GivesNothing()
    {
        IUserMailHistoryService service = new PlainHistory();

        Assert.Null(await service.GetSentAsync("any", TestContext.Current.CancellationToken));
    }

    private sealed class PlainHistory : IUserMailHistoryService
    {
        public Task<UserMailHistoryPage> GetMineAsync(string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default) => Task.FromResult(new UserMailHistoryPage());

        public Task<UserMailHistoryPage> GetForUserAsync(string userKey, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default) => Task.FromResult(new UserMailHistoryPage());
    }

    private sealed class FixedPrincipal(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
