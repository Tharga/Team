using Microsoft.Extensions.Logging;
using Tharga.Team.Email.Users;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>
/// A mail the queue refuses is recorded as given up, so the mail log shows an attempt that would otherwise leave no trace, while
/// the caller hears the same exception as before; it is recorded once a day per cause, never with its body, and never in the
/// user's own history.
/// </summary>
public class UserMailRefusalTests
{
    private readonly UserMailFixture _mail = new();

    private static UserMail Mail(string category = UserMailFixture.News, string subject = "Hello", string userKey = UserMailFixture.UserKey) => new()
    {
        RecipientUserKey = userKey,
        Category = category,
        Subject = subject,
        Body = "Hi."
    };

    private UserMailQueue QueueWithDispatcher() => new(_mail.Categories, _mail.Outbox, _mail.Wrapped, _mail.Time, _mail.Dispatcher());

    [Fact]
    public async Task AnUnknownCategory_IsRefusedAsBefore_AndRecordedAsGivenUp()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _mail.Queue.QueueAsync(Mail("nope", userKey: " user-1 ")));

        var record = _mail.Outbox.Single;
        Assert.True(record.Refused);
        Assert.Equal(UserMailStatus.Failed, record.Status);
        Assert.Equal(0, record.Attempts);
        Assert.Equal(UserMailFixture.Start, record.CompletedAt);
        Assert.Equal(("user-1", "nope", "Hello", UserMailKind.Mail), (record.Mail.RecipientUserKey, record.Mail.Category, record.Mail.Subject, record.Mail.Kind));
        Assert.Contains("nope", record.Reason);
        Assert.DoesNotContain("(Parameter", record.Reason);
    }

    [Fact]
    public async Task TheBodyAndFiles_AreNeverRecorded()
    {
        var mail = Mail("nope") with { BodyHtml = "<p>Secret</p>", Attachments = [new UserMailAttachment { FileName = "a.pdf", ContentType = "application/pdf", Content = [1, 2, 3] }] };

        await Assert.ThrowsAsync<ArgumentException>(() => _mail.Queue.QueueAsync(mail));

        var record = _mail.Outbox.Single.Mail;
        Assert.Null(record.Body);
        Assert.Null(record.BodyHtml);
        Assert.Empty(record.Attachments);
    }

    [Fact]
    public async Task AMailWithNoSubject_IsRecordedWithNone_AndTheReason()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _mail.Queue.QueueAsync(Mail(subject: "  ")));

        var record = _mail.Outbox.Single;
        Assert.Equal(string.Empty, record.Mail.Subject);
        Assert.Equal("A mail has a subject.", record.Reason);
    }

    [Fact]
    public async Task AnOptionalCategoryToAnAddressAlone_IsRecordedWithTheAddress()
    {
        var mail = new UserMail { RecipientAddress = " Bea@Example.org ", RecipientName = "Bea", Category = UserMailFixture.News, Subject = "Hello", Body = "Hi." };

        await Assert.ThrowsAsync<ArgumentException>(() => _mail.Queue.QueueAsync(mail));

        var record = _mail.Outbox.Single;
        Assert.Null(record.Mail.RecipientUserKey);
        Assert.Equal("bea@example.org", record.RecipientAddress);
        Assert.Equal("Bea", record.Mail.RecipientName);
        Assert.Contains("required category", record.Reason);
    }

    [Fact]
    public async Task ABodyOverTheLimit_IsRecordedWithoutTheBody()
    {
        var mail = Mail() with { Body = new string('x', _mail.Options.MaxBodyLength + 1) };

        await Assert.ThrowsAsync<ArgumentException>(() => _mail.Queue.QueueAsync(mail));

        var record = _mail.Outbox.Single;
        Assert.Null(record.Mail.Body);
        Assert.Contains("longer than", record.Reason);
    }

    [Fact]
    public async Task AMailSentAtOnce_IsRecordedWhenRefused_Too()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => QueueWithDispatcher().SendNowAsync(Mail("nope")));

        Assert.True(_mail.Outbox.Single.Refused);
    }

    [Fact]
    public async Task ABatchRefused_IsRecordedOnce_WithItsKindCategoryAndTeam_AndNoRecipient()
    {
        var batch = new UserMailBatch { RecipientUserKeys = ["a", "b"], Category = "nope", Subject = "Maintenance", Body = "B", TeamKey = "acme" };

        await Assert.ThrowsAsync<ArgumentException>(() => _mail.Queue.QueueBatchAsync(batch));

        var record = _mail.Outbox.Single;
        Assert.Null(record.Mail.RecipientUserKey);
        Assert.Equal((UserMailKind.Message, "nope", "Maintenance", "acme"), (record.Mail.Kind, record.Mail.Category, record.Mail.Subject, record.Mail.TeamKey));
        Assert.True(record.Refused);
    }

    [Fact]
    public async Task ABatchNamingNobody_IsRecorded()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _mail.Queue.QueueBatchAsync(new UserMailBatch { RecipientUserKeys = [], Category = UserMailFixture.News, Subject = "S", Body = "B" }));

        Assert.Contains("at least one", _mail.Outbox.Single.Reason);
    }

    [Fact]
    public async Task ContactMailQueuedHere_IsRefusedAndRecordedAsContact()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _mail.Queue.QueueAsync(Mail() with { Kind = UserMailKind.Contact }));

        var record = _mail.Outbox.Single;
        Assert.Equal(UserMailKind.Contact, record.Mail.Kind);
        Assert.Equal(UserMailQueue.ContactRefusal, record.Reason);
    }

    [Fact]
    public async Task NothingToRecord_WhenThereIsNoMail()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _mail.Queue.QueueAsync(null));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _mail.Queue.QueueBatchAsync(null));

        Assert.Empty(_mail.Outbox.Items);
    }

    [Fact]
    public async Task AMailThatIsAccepted_LeavesNoRefusal()
    {
        await _mail.Queue.QueueAsync(Mail());

        Assert.False(_mail.Outbox.Single.Refused);
        Assert.Equal(UserMailStatus.Queued, _mail.Outbox.Single.Status);
    }

    [Fact]
    public async Task TheSameCause_IsRecordedOnceADay_AndAgainTheNextDay()
    {
        for (var i = 0; i < 3; i++) await Assert.ThrowsAsync<ArgumentException>(() => _mail.Queue.QueueAsync(Mail("nope")));
        Assert.Single(_mail.Outbox.Items);

        _mail.Time.Advance(TimeSpan.FromDays(1));
        await Assert.ThrowsAsync<ArgumentException>(() => _mail.Queue.QueueAsync(Mail("nope")));

        Assert.Equal(2, _mail.Outbox.Items.Count);
    }

    [Fact]
    public async Task AnotherCause_AnotherRecipient_OrAnotherReason_IsRecordedOnItsOwn()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _mail.Queue.QueueAsync(Mail("nope")));
        await Assert.ThrowsAsync<ArgumentException>(() => _mail.Queue.QueueAsync(Mail("nope", userKey: "user-2")));
        await Assert.ThrowsAsync<ArgumentException>(() => _mail.Queue.QueueAsync(Mail(subject: " ")));

        Assert.Equal(3, _mail.Outbox.Items.Count);
    }

    [Fact]
    public async Task WhatIsRecorded_IsCutShort()
    {
        var mail = Mail(new string('c', 1000), new string('s', 1000), new string('u', 1000));

        await Assert.ThrowsAsync<ArgumentException>(() => _mail.Queue.QueueAsync(mail));

        var record = _mail.Outbox.Single;
        Assert.Equal(200, record.Mail.Category.Length);
        Assert.Equal(200, record.Mail.Subject.Length);
        Assert.Equal(200, record.Mail.RecipientUserKey.Length);
        Assert.Equal(500, record.Reason.Length);
    }

    [Fact]
    public async Task ARefusal_IsNotInTheUsersOwnHistory_ButIsInTheLog()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _mail.Queue.QueueAsync(Mail("nope")));

        var history = await _mail.Queue.ListForRecipientAsync(UserMailFixture.UserKey, UserMailFixture.Address, cancellationToken: TestContext.Current.CancellationToken);
        var log = await _mail.Queue.ListAsync(new UserMailFilter { Status = UserMailStatus.Failed }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(history.Items);
        var found = Assert.Single(log.Items);
        Assert.Equal("nope", found.Category);
        Assert.Contains("nope", found.Reason);
    }

    [Fact]
    public async Task AFailureToRecord_NeverChangesWhatTheCallerHears_AndIsLogged()
    {
        var outbox = Substitute.For<IUserMailOutbox>();
        outbox.AddRefusedAsync(default, default, default, default, default).ReturnsForAnyArgs<Task<bool>>(_ => throw new TimeoutException("The database is down."));
        var logger = new CapturingLogger();
        var queue = new UserMailQueue(_mail.Categories, outbox, _mail.Wrapped, _mail.Time, null, logger);

        var e = await Assert.ThrowsAsync<ArgumentException>(() => queue.QueueAsync(Mail("nope")));

        Assert.Contains("nope", e.Message);
        Assert.Equal([LogLevel.Warning], logger.Levels);
    }

    [Fact]
    public async Task AnOutboxThatKeepsNoSuchRecord_IsNotAnError()
    {
        var outbox = Substitute.For<IUserMailOutbox>();
        outbox.AddRefusedAsync(default, default, default, default, default).ReturnsForAnyArgs<Task<bool>>(_ => throw new NotSupportedException());
        var logger = new CapturingLogger();
        var queue = new UserMailQueue(_mail.Categories, outbox, _mail.Wrapped, _mail.Time, null, logger);

        await Assert.ThrowsAsync<ArgumentException>(() => queue.QueueAsync(Mail("nope")));

        Assert.Empty(logger.Levels);
    }

    private sealed class CapturingLogger : ILogger<UserMailQueue>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) => Levels.Add(logLevel);
    }
}
