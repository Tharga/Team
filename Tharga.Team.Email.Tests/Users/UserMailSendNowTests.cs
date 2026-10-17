using Tharga.Team.Email.Users;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>A mail someone is waiting on is recorded like any other and tried once, at once, and its outcome is returned rather than retried later.</summary>
public class UserMailSendNowTests
{
    private readonly UserMailFixture _fixture = new();

    private UserMailQueue Queue() => new(_fixture.Categories, _fixture.Outbox, _fixture.Wrapped, _fixture.Time, _fixture.Dispatcher());

    private static UserMail ToAddress() => new()
    {
        RecipientAddress = "bea@example.org",
        Category = UserMailCategories.AccountKey,
        Subject = "Welcome",
        Body = "Hello",
        Kind = UserMailKind.Invitation
    };

    [Fact]
    public async Task ASentMail_IsRecordedAndReportedAsSent()
    {
        var delivery = await Queue().SendNowAsync(ToAddress());

        Assert.Equal(UserMailStatus.Sent, delivery.Status);
        Assert.Equal(UserMailKind.Invitation, _fixture.Outbox.Single.Mail.Kind);
        Assert.Single(_fixture.Mail.Sent);
    }

    [Fact]
    public async Task AMailThatCouldNotGo_IsReportedWithItsReason_AndNeverTriedAgain()
    {
        _fixture.Mail.Respond = _ => MailSendResult.Failed("The server said no.");

        var delivery = await Queue().SendNowAsync(ToAddress());

        Assert.Equal(UserMailStatus.Failed, delivery.Status);
        Assert.Contains("The server said no.", delivery.Reason);
        Assert.Equal(1, _fixture.Outbox.Single.Attempts);
    }

    [Fact]
    public async Task AQueueBuiltWithoutTheDispatcher_SaysSoRatherThanQueueingQuietly()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.Queue.SendNowAsync(ToAddress()));
        Assert.Empty(_fixture.Outbox.Items);
    }
}
