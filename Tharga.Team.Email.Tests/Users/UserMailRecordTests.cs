using System.Security.Claims;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Users;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>
/// Mail sent outside the queue — a support answer, a receipt — is in the global log as its own kind, with the case it
/// belongs to, and stays out of a user's own history.
/// </summary>
public class UserMailRecordTests
{
    private readonly UserMailFixture _mail = new();

    private UserMailHistoryService Service()
    {
        var users = Substitute.For<IUserService>();
        var user = Substitute.For<IUser>();
        user.Key.Returns(UserMailFixture.UserKey);
        users.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(Task.FromResult(user));

        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Sam"), new Claim(TeamClaimTypes.SystemScope, SystemUserScopes.Manage)], "Test"));

        return new UserMailHistoryService(
            _mail.Queue,
            _mail.Recipients,
            new TeamAuthorizer(new FixedPrincipal(principal)),
            users,
            new CompositeAuditLogger([_mail.Audit], Options.Create(new AuditOptions())),
            new AuditEntryFactory(null));
    }

    private UserMailRecord Answer() => new()
    {
        Kind = UserMailKind.SupportAnswer,
        RecipientAddress = UserMailFixture.Address,
        Subject = "Re: Export is empty",
        TeamKey = "acme",
        SentByUserKey = "staff-1",
        SentByName = "Sam Staff",
        Headers = new UserMailHeaders("support@example.net", "Example Support", "case-1@example.net"),
        Status = UserMailStatus.Sent,
        MessageId = "<answer-2@example.net>",
        At = _mail.Time.GetUtcNow().UtcDateTime,
        CaseId = "case-1",
        CaseMessageSequence = 2
    };

    private UserMailRecord Receipt() => Answer() with
    {
        Kind = UserMailKind.Receipt,
        Subject = "Re: Question",
        TeamKey = null,
        SentByUserKey = null,
        SentByName = null,
        MessageId = "<receipt-1@example.net>",
        CaseId = "case-2",
        CaseMessageSequence = null
    };

    [Fact]
    public async Task AnAnswer_IsInTheLog_WithItsSenderHeadersAndCase_AndNoCategory()
    {
        await _mail.Outbox.RecordAsync(Answer(), TestContext.Current.CancellationToken);

        var item = Assert.Single((await Service().ListAllAsync(cancellationToken: TestContext.Current.CancellationToken)).Items);

        Assert.Equal((UserMailKind.SupportAnswer, UserMailStatus.Sent, "Re: Export is empty"), (item.Kind, item.Status, item.Subject));
        Assert.Null(item.Category);
        Assert.Equal(UserMailFixture.Address, item.Address);
        Assert.Equal(("acme", "staff-1", "Sam Staff"), (item.TeamKey, item.SentByUserKey, item.SentByName));
        Assert.Equal(("support@example.net", "Example Support", "case-1@example.net"), (item.FromAddress, item.FromName, item.ReplyTo));
        Assert.Equal(("case-1", (int?)2), (item.CaseId, item.CaseMessageSequence));
    }

    [Fact]
    public async Task TheLog_FiltersByTheNewKinds()
    {
        await _mail.QueueAsync();
        await _mail.Outbox.RecordAsync(Answer(), TestContext.Current.CancellationToken);
        await _mail.Outbox.RecordAsync(Receipt(), TestContext.Current.CancellationToken);

        var answers = await Service().ListAllAsync(new UserMailFilter { Kind = UserMailKind.SupportAnswer }, cancellationToken: TestContext.Current.CancellationToken);
        var receipts = await Service().ListAllAsync(new UserMailFilter { Kind = UserMailKind.Receipt }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Re: Export is empty", Assert.Single(answers.Items).Subject);
        Assert.Equal(("Re: Question", "case-2", (int?)null), (Assert.Single(receipts.Items).Subject, receipts.Items[0].CaseId, receipts.Items[0].CaseMessageSequence));
    }

    [Fact]
    public async Task TheDelivery_CarriesTheCaseReference()
    {
        var id = await _mail.Outbox.RecordAsync(Answer(), TestContext.Current.CancellationToken);

        var delivery = await _mail.Queue.GetAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal(("case-1", (int?)2), (delivery.CaseId, delivery.CaseMessageSequence));
        Assert.Equal((1, UserMailStatus.Sent), (delivery.Attempts, delivery.Status));
    }

    [Fact]
    public async Task AnAnswerOrReceiptToAUsersAddress_IsNotInTheirOwnHistory()
    {
        await _mail.Outbox.RecordAsync(Answer(), TestContext.Current.CancellationToken);
        await _mail.Outbox.RecordAsync(Receipt(), TestContext.Current.CancellationToken);

        var mine = await Service().GetMineAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(mine.Items);
    }

    private sealed class FixedPrincipal(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
