using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Email.Users;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>
/// Every registered outcome sink is told, once, when a mail to a user is given up or bounces: not when it is skipped, not
/// while it waits to be tried again, and not when another sender recorded the outcome first. A sink that throws never
/// stops the others or changes the mail.
/// </summary>
public class MailOutcomeTests
{
    private const string First = "instance-a";
    private const string Second = "instance-b";
    private const string Staff = "staff-1";
    private const string Acme = "acme";
    private const string BounceReason = "550 5.1.1 Recipient address rejected";

    private static Task<UserMailQueued> QueueFromStaffAsync(UserMailFixture fixture, string category = UserMailFixture.News)
        => fixture.Queue.QueueAsync(new UserMail { RecipientUserKey = UserMailFixture.UserKey, Category = category, Subject = "Hello", Body = "Hi.", TeamKey = Acme, SentByUserKey = Staff, SentByName = "Sam Staff" });

    private static MailBounce Report(string messageId, string reason = BounceReason)
        => new() { OriginalMessageId = messageId, Reason = reason, ReceivedAt = UserMailFixture.Start.AddHours(1) };

    [Fact]
    public async Task GivingUp_TellsTheSinksOnce_WithTheSenderAndTheTeam()
    {
        var fixture = new UserMailFixture(o => o.MaxAttempts = 3);
        fixture.Mail.Respond = _ => MailSendResult.Failed("550 mailbox unavailable");
        var queued = await QueueFromStaffAsync(fixture);
        var dispatcher = fixture.Dispatcher();

        for (var i = 0; i < 4; i++)
        {
            fixture.Time.Advance(TimeSpan.FromHours(1));
            await dispatcher.SendNextAsync(First, default);
        }

        var outcome = Assert.Single(fixture.Outcomes.Told);
        Assert.Equal((queued.Id, UserMailKind.Mail, UserMailStatus.Failed), (outcome.Id, outcome.Kind, outcome.Status));
        Assert.Equal("Gave up after 3 attempts. 550 mailbox unavailable", outcome.Reason);
        Assert.Equal((Staff, Acme), (outcome.SentByUserKey, outcome.TeamKey));
        Assert.Equal(((string)null, (int?)null), (outcome.CaseId, outcome.CaseMessageSequence));
        Assert.Equal(UserMailFixture.Start.AddHours(3), outcome.At);
    }

    [Fact]
    public async Task AFailureThatWillBeTriedAgain_TellsNobody()
    {
        var fixture = new UserMailFixture();
        fixture.Mail.Respond = _ => MailSendResult.Failed("451 try later");
        await QueueFromStaffAsync(fixture);

        Assert.Equal(UserMailStatus.Queued, await fixture.Dispatcher().SendNextAsync(First, default));

        Assert.Empty(fixture.Outcomes.Told);
    }

    [Fact]
    public async Task APermanentFailure_TellsTheSinksAtOnce()
    {
        var fixture = new UserMailFixture();
        fixture.Recipients.Remove(UserMailFixture.UserKey);
        await QueueFromStaffAsync(fixture);

        Assert.Equal(UserMailStatus.Failed, await fixture.Dispatcher().SendNextAsync(First, default));

        var outcome = Assert.Single(fixture.Outcomes.Told);
        Assert.Equal((UserMailStatus.Failed, "No user has this key.", Staff), (outcome.Status, outcome.Reason, outcome.SentByUserKey));
    }

    [Fact]
    public async Task AMailWithheldByThePolicy_TellsTheSinks()
    {
        var fixture = new UserMailFixture();
        fixture.Policy.Decide(Arg.Any<string>(), Arg.Any<string>()).Returns(x => new OutboundMailDecision(OutboundMailAction.Withhold, x.ArgAt<string>(0), x.ArgAt<string>(1)));
        await QueueFromStaffAsync(fixture);

        await fixture.Dispatcher().SendNextAsync(First, default);

        Assert.Equal(UserMailDispatcher.WithheldReason, Assert.Single(fixture.Outcomes.Told).Reason);
    }

    [Fact]
    public async Task ASkippedMail_IsNoFailure_AndTellsNobody()
    {
        var fixture = new UserMailFixture();
        await QueueFromStaffAsync(fixture, UserMailFixture.Digest);

        Assert.Equal(UserMailStatus.Skipped, await fixture.Dispatcher().SendNextAsync(First, default));

        Assert.Empty(fixture.Outcomes.Told);
    }

    [Fact]
    public async Task AFailureAnotherSenderRecordedFirst_TellsNobodyHere()
    {
        var fixture = new UserMailFixture();
        await QueueFromStaffAsync(fixture);
        var stale = await fixture.Outbox.ClaimNextAsync(First, fixture.Time.GetUtcNow().UtcDateTime, TimeSpan.FromMinutes(1));
        fixture.Time.Advance(TimeSpan.FromMinutes(2));
        await fixture.Outbox.ClaimNextAsync(Second, fixture.Time.GetUtcNow().UtcDateTime, TimeSpan.FromMinutes(5));
        fixture.Recipients.Remove(UserMailFixture.UserKey);

        await fixture.Dispatcher().SendNowAsync(stale, First, default);

        Assert.Empty(fixture.Outcomes.Told);
    }

    [Fact]
    public async Task ABounce_TellsTheSinksOnce_AndASecondReadOfItTellsNobody()
    {
        var fixture = new UserMailFixture();
        var queued = await QueueFromStaffAsync(fixture);
        await fixture.Dispatcher().RunAsync(First, default);
        var handler = new UserMailBounceHandler(fixture.Outbox, null, fixture.Notifier);
        var messageId = fixture.Outbox.Single.MessageId;

        await handler.HandleAsync(Report(messageId), default);
        await handler.HandleAsync(Report(messageId, "421 later wording"), default);

        var outcome = Assert.Single(fixture.Outcomes.Told);
        Assert.Equal((queued.Id, UserMailKind.Mail, UserMailStatus.Bounced, BounceReason), (outcome.Id, outcome.Kind, outcome.Status, outcome.Reason));
        Assert.Equal((Staff, Acme), (outcome.SentByUserKey, outcome.TeamKey));
        Assert.Equal(UserMailFixture.Start.AddHours(1), outcome.At);
    }

    [Fact]
    public async Task AReceiptThatBounces_TellsTheSinks_WithItsCase_AndNoSender()
    {
        var fixture = new UserMailFixture();
        var id = await fixture.Outbox.RecordAsync(new UserMailRecord
        {
            Kind = UserMailKind.Receipt,
            RecipientAddress = "visitor@example.com",
            Subject = "Re: Question",
            Status = UserMailStatus.Sent,
            MessageId = "receipt-1@example.net",
            At = UserMailFixture.Start,
            CaseId = "case-1"
        });

        await new UserMailBounceHandler(fixture.Outbox, null, fixture.Notifier).HandleAsync(Report("receipt-1@example.net"), default);

        var outcome = Assert.Single(fixture.Outcomes.Told);
        Assert.Equal((id, UserMailKind.Receipt, UserMailStatus.Bounced), (outcome.Id, outcome.Kind, outcome.Status));
        Assert.Equal(("case-1", (string)null, (string)null), (outcome.CaseId, outcome.SentByUserKey, outcome.TeamKey));
    }

    [Fact]
    public async Task ABounceForAMailNobodyKnows_TellsNobody()
    {
        var fixture = new UserMailFixture();

        Assert.False(await new UserMailBounceHandler(fixture.Outbox, null, fixture.Notifier).HandleAsync(Report("someone-else@example.net"), default));

        Assert.Empty(fixture.Outcomes.Told);
    }

    [Fact]
    public async Task ASinkThatThrows_NeitherStopsTheOthers_NorChangesTheMail()
    {
        var fixture = new UserMailFixture();
        fixture.Recipients.Remove(UserMailFixture.UserKey);
        await QueueFromStaffAsync(fixture);
        var broken = Substitute.For<IMailOutcomeSink>();
        broken.OnMailOutcomeAsync(Arg.Any<MailOutcome>(), Arg.Any<CancellationToken>()).Returns<Task>(_ => throw new InvalidOperationException("sink down"));
        var dispatcher = new UserMailDispatcher(fixture.Outbox, fixture.Categories, fixture.Preferences, fixture.Recipients, fixture.Mail, fixture.Policy, fixture.Footer(), fixture.Wrapped, fixture.Time,
            outcomes: new MailOutcomeNotifier([broken, fixture.Outcomes]));

        var status = await dispatcher.SendNextAsync(First, default);

        Assert.Equal(UserMailStatus.Failed, status);
        Assert.Equal(UserMailStatus.Failed, fixture.Outbox.Single.Status);
        Assert.Single(fixture.Outcomes.Told);
        await broken.Received(1).OnMailOutcomeAsync(Arg.Any<MailOutcome>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNoSinks_AFailureIsRecordedAsBefore()
    {
        var fixture = new UserMailFixture();
        fixture.Recipients.Remove(UserMailFixture.UserKey);
        await QueueFromStaffAsync(fixture);
        var dispatcher = new UserMailDispatcher(fixture.Outbox, fixture.Categories, fixture.Preferences, fixture.Recipients, fixture.Mail, fixture.Policy, fixture.Footer(), fixture.Wrapped, fixture.Time,
            outcomes: new MailOutcomeNotifier([]));

        Assert.Equal(UserMailStatus.Failed, await dispatcher.SendNextAsync(First, default));
        Assert.Equal(UserMailStatus.Failed, fixture.Outbox.Single.Status);
    }

    [Fact]
    public async Task TheRegisteredBounceHandler_TellsTheRegisteredSinks()
    {
        var fixture = new UserMailFixture();
        await QueueFromStaffAsync(fixture);
        await fixture.Dispatcher().RunAsync(First, default);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddThargaUserMail(o => o.BaseUrl = "https://app.example.com");
        services.AddSingleton<IUserMailOutbox>(fixture.Outbox);
        var sink = new RecordingMailOutcomeSink();
        services.AddSingleton<IMailOutcomeSink>(sink);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        foreach (var handler in scope.ServiceProvider.GetServices<IMailBounceHandler>()) await handler.HandleAsync(Report(fixture.Outbox.Single.MessageId), default);

        Assert.Equal(UserMailStatus.Bounced, Assert.Single(sink.Told).Status);
    }

    [Fact]
    public async Task TheRegisteredNotifier_WithNoSinks_DoesNothing()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddThargaUserMail(o => o.BaseUrl = "https://app.example.com");

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<MailOutcomeNotifier>().NotifyAsync(new MailOutcome { Kind = UserMailKind.Mail, Status = UserMailStatus.Failed, At = UserMailFixture.Start }, default);
    }
}
