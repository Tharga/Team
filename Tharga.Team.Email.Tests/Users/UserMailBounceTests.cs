using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Email.Users;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>A bounce report naming a sent user mail marks it bounced with the receiving server's reason, once, and moves it from sent to bounced in its message's counts; a report naming nothing known is left alone.</summary>
public class UserMailBounceTests
{
    private const string Reason = "550 5.1.1 Recipient address rejected";

    private static readonly UserMessageRecipients Alpha = new() { TeamKeys = [UserMessageFixture.Alpha], EveryMember = true };

    private static UserMailBounceHandler Handler(UserMailFixture fixture) => new(fixture.Outbox);

    private static MailBounce Report(string messageId, string reason = Reason, DateTime? at = null)
        => new() { OriginalMessageId = messageId, Reason = reason, ReceivedAt = at ?? UserMailFixture.Start.AddHours(1) };

    private static async Task<UserMailItem> SentAsync(UserMailFixture fixture)
    {
        await fixture.QueueAsync();
        await fixture.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);

        return fixture.Outbox.Single;
    }

    [Fact]
    public async Task AReportNamingASentMail_MarksItBounced_WithTheServersReason_AndWhen()
    {
        var fixture = new UserMailFixture();
        var sent = await SentAsync(fixture);

        var recognised = await Handler(fixture).HandleAsync(Report(sent.MessageId.Trim('<', '>')), TestContext.Current.CancellationToken);

        Assert.True(recognised);
        var delivery = await fixture.Queue.GetAsync(sent.Id);
        Assert.Equal(UserMailStatus.Bounced, delivery.Status);
        Assert.Equal(Reason, delivery.Reason);
        Assert.Equal(UserMailFixture.Start.AddHours(1), delivery.BouncedAt);
        Assert.Equal(UserMailFixture.Start.AddHours(1), delivery.ChangedAt);
        Assert.Equal(sent.MessageId, delivery.MessageId);
    }

    [Fact]
    public async Task AReportInAngleBrackets_FindsTheSameMail()
    {
        var fixture = new UserMailFixture();
        var sent = await SentAsync(fixture);

        Assert.True(await Handler(fixture).HandleAsync(Report($" {sent.MessageId} "), TestContext.Current.CancellationToken));
        Assert.Equal(UserMailStatus.Bounced, fixture.Outbox.Single.Status);
    }

    [Fact]
    public async Task ASecondReportForTheSameMail_IsRecognised_AndChangesNothing()
    {
        var fixture = new UserMailFixture();
        var sent = await SentAsync(fixture);
        var handler = Handler(fixture);
        await handler.HandleAsync(Report(sent.MessageId), TestContext.Current.CancellationToken);

        var again = await handler.HandleAsync(Report(sent.MessageId, "421 later wording", UserMailFixture.Start.AddDays(1)), TestContext.Current.CancellationToken);

        Assert.True(again);
        Assert.Equal(Reason, fixture.Outbox.Single.Reason);
        Assert.Equal(UserMailFixture.Start.AddHours(1), fixture.Outbox.Single.BouncedAt);
    }

    [Fact]
    public async Task AReportNamingNoKnownMail_IsNotRecognised_AndChangesNothing()
    {
        var fixture = new UserMailFixture();
        await SentAsync(fixture);

        Assert.False(await Handler(fixture).HandleAsync(Report("someone-else@example.net"), TestContext.Current.CancellationToken));
        Assert.False(await Handler(fixture).HandleAsync(Report(" "), TestContext.Current.CancellationToken));
        Assert.Equal(UserMailStatus.Sent, fixture.Outbox.Single.Status);
    }

    [Fact]
    public async Task AReportGivingNoReason_StillSaysTheMailCameBack()
    {
        var fixture = new UserMailFixture();
        var sent = await SentAsync(fixture);

        await Handler(fixture).HandleAsync(Report(sent.MessageId, reason: null), TestContext.Current.CancellationToken);

        Assert.Equal(UserMailBounceHandler.DefaultReason, fixture.Outbox.Single.Reason);
    }

    [Fact]
    public async Task AMailThatWasNeverSent_CannotBounce()
    {
        var fixture = new UserMailFixture();
        await fixture.QueueAsync();
        fixture.Recipients.Remove(UserMailFixture.UserKey);
        await fixture.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);

        Assert.False(await Handler(fixture).HandleAsync(Report("m1@example.com"), TestContext.Current.CancellationToken));
        Assert.Equal(UserMailStatus.Failed, fixture.Outbox.Single.Status);
    }

    [Fact]
    public async Task ABouncedMailOfAMessage_MovesFromSentToBouncedInItsCounts_AndShowsAmongItsRecipients()
    {
        var fixture = new UserMessageFixture();
        var message = await fixture.Service.SendAsync(UserMessageFixture.Message(Alpha), TestContext.Current.CancellationToken);
        await fixture.Mail.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);
        var anna = fixture.Mail.Outbox.Items.Single(x => x.Mail.RecipientUserKey == "anna");

        await Handler(fixture.Mail).HandleAsync(Report(anna.MessageId), TestContext.Current.CancellationToken);

        var sent = await fixture.Service.GetSentMessageAsync(message.Id, TestContext.Current.CancellationToken);
        Assert.Equal(new UserMailOutcomes { Sent = 2, Bounced = 1 }, sent.Outcomes);

        var bounced = (await fixture.Service.GetRecipientsAsync(message.Id, UserMailStatus.Bounced, cancellationToken: TestContext.Current.CancellationToken)).Items;
        var recipient = Assert.Single(bounced);
        Assert.Equal("anna", recipient.UserKey);
        Assert.Equal(Reason, recipient.Reason);
        Assert.Equal("anna@example.org", recipient.Address);
    }

    [Fact]
    public async Task ABouncedMail_ShowsAsBouncedInTheUsersHistory()
    {
        var fixture = new UserMailFixture();
        var sent = await SentAsync(fixture);
        await Handler(fixture).HandleAsync(Report(sent.MessageId), TestContext.Current.CancellationToken);

        var page = await fixture.Queue.ListForRecipientAsync(UserMailFixture.UserKey, UserMailFixture.Address, cancellationToken: TestContext.Current.CancellationToken);

        var item = Assert.Single(page.Items);
        Assert.Equal(UserMailStatus.Bounced, item.Status);
        Assert.Equal(Reason, item.Reason);
    }

    [Fact]
    public void MailToUsers_RegistersItsBounceHandlerOnce()
    {
        var services = new ServiceCollection();
        services.AddThargaUserMail();
        services.AddThargaUserMail();
        services.AddSingleton<IUserMailOutbox>(new InMemoryUserMailOutbox());

        using var scope = services.BuildServiceProvider().CreateScope();

        Assert.IsType<UserMailBounceHandler>(Assert.Single(scope.ServiceProvider.GetServices<IMailBounceHandler>()));
    }
}
