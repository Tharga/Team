using Tharga.Team.Email.Users;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>One mail to many users: the content is checked as a single mail's is and stored once, one mail is queued per recipient, and each is sent with the batch's content after its recipient's choice is read again.</summary>
public class UserMailBatchTests
{
    private static UserMailBatch Batch(string[] recipients, string category = UserMailFixture.News, UserMailAttachment[] attachments = null) => new()
    {
        RecipientUserKeys = recipients,
        Category = category,
        Subject = "News\r\n of the week",
        BodyHtml = "<p>Hello <script>alert(1)</script><b>all</b></p>",
        Attachments = attachments ?? []
    };

    private static UserMailFixture WithUsers(params string[] keys)
    {
        var fixture = new UserMailFixture();
        foreach (var key in keys) fixture.Recipients.Add(new UserMailRecipient { Key = key, EMail = $"{key}@example.org", Name = key.ToUpperInvariant() });
        return fixture;
    }

    [Fact]
    public async Task EachRecipient_IsQueuedOnce_AndTheContentIsStoredOnce()
    {
        var fixture = WithUsers("u-1", "u-2");
        var file = new UserMailAttachment { FileName = "notes.pdf", ContentType = "application/pdf", Content = [1, 2, 3] };

        var queued = await fixture.Queue.QueueBatchAsync(Batch(["u-1", " u-2 ", "u-1", ""], attachments: [file]), TestContext.Current.CancellationToken);

        Assert.Equal(2, queued.Recipients);
        var batch = Assert.Single(fixture.Outbox.Batches);
        Assert.Equal(queued.Id, batch.Key);
        Assert.Equal([3], batch.Value.Attachments.Select(x => x.Size));
        Assert.Equal(["u-1", "u-2"], fixture.Outbox.Items.Select(x => x.Mail.RecipientUserKey));
        Assert.All(fixture.Outbox.Items, x =>
        {
            Assert.Equal(queued.Id, x.BatchId);
            Assert.Empty(x.Mail.Attachments);
            Assert.Null(x.Mail.BodyHtml);
        });
    }

    [Fact]
    public async Task TheContent_IsSanitizedAndItsTextDerived_AsASingleMailsIs()
    {
        var fixture = WithUsers("u-1");

        await fixture.Queue.QueueBatchAsync(Batch(["u-1"]), TestContext.Current.CancellationToken);

        var content = Assert.Single(fixture.Outbox.Batches).Value;
        Assert.Equal("News of the week", content.Subject);
        Assert.DoesNotContain("script", content.BodyHtml);
        Assert.Contains("Hello", content.Body);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ,")]
    public async Task ABatchNamingNobody_IsRefused(string recipients)
    {
        var fixture = WithUsers();

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Queue.QueueBatchAsync(Batch(recipients.Split(',')), TestContext.Current.CancellationToken));

        Assert.Empty(fixture.Outbox.Batches);
    }

    [Fact]
    public async Task AnUnknownCategory_IsRefused()
    {
        var fixture = WithUsers("u-1");

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Queue.QueueBatchAsync(Batch(["u-1"], "nonsense"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FilesOverTheLimit_AreRefused()
    {
        var fixture = new UserMailFixture(o => o.MaxAttachmentBytes = 2);
        var file = new UserMailAttachment { FileName = "big.bin", ContentType = "application/octet-stream", Content = [1, 2, 3] };

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Queue.QueueBatchAsync(Batch(["u-1"], attachments: [file]), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EveryMailOfTheBatch_IsSentWithItsContent_ReadOncePerPass()
    {
        var fixture = WithUsers("u-1", "u-2", "u-3");
        var file = new UserMailAttachment { FileName = "notes.pdf", ContentType = "application/pdf", Content = [1, 2, 3] };
        await fixture.Queue.QueueBatchAsync(Batch(["u-1", "u-2", "u-3"], attachments: [file]), TestContext.Current.CancellationToken);

        Assert.Equal(3, await fixture.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken));

        Assert.Equal(1, fixture.Outbox.BatchContentReads);
        Assert.Equal(3, fixture.Mail.Sent.Count);
        Assert.All(fixture.Mail.Sent, x =>
        {
            Assert.Equal("News of the week", x.Subject);
            Assert.Contains("<b>all</b>", x.BodyHtml);
            Assert.Equal("notes.pdf", Assert.Single(x.Attachments).FileName);
            Assert.Equal("support@example.com", x.ReplyTo);
        });
        Assert.All(fixture.Outbox.Items, x => Assert.Equal(UserMailStatus.Sent, x.Status));
    }

    [Fact]
    public async Task ARecipientWhoTurnedTheCategoryOffAfterQueuing_IsSkippedAtSendTime()
    {
        var fixture = WithUsers("u-1", "u-2");
        await fixture.Queue.QueueBatchAsync(Batch(["u-1", "u-2"]), TestContext.Current.CancellationToken);
        await fixture.Store.SetChoiceAsync("u-2", UserMailFixture.News, false, TestContext.Current.CancellationToken);

        await fixture.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);

        Assert.Equal(UserMailStatus.Sent, fixture.Outbox.Items.Single(x => x.Mail.RecipientUserKey == "u-1").Status);
        Assert.Equal(UserMailStatus.Skipped, fixture.Outbox.Items.Single(x => x.Mail.RecipientUserKey == "u-2").Status);
        Assert.Single(fixture.Mail.Sent);
    }

    [Fact]
    public async Task AMailWhoseBatchIsGone_FailsWithTheReason()
    {
        var fixture = WithUsers("u-1");
        var queued = await fixture.Queue.QueueBatchAsync(Batch(["u-1"]), TestContext.Current.CancellationToken);
        fixture.Outbox.ForgetBatch(queued.Id);

        Assert.Equal(UserMailStatus.Failed, await fixture.Dispatcher().SendNextAsync("me", TestContext.Current.CancellationToken));

        Assert.Equal(UserMailDispatcher.BatchGoneReason, fixture.Outbox.Single.Reason);
        Assert.Empty(fixture.Mail.Sent);
    }

    [Fact]
    public async Task TheBatchReportsWhatBecameOfItsMails()
    {
        var fixture = WithUsers("u-1", "u-2");
        fixture.Recipients.Add(new UserMailRecipient { Key = "u-3" });
        var queued = await fixture.Queue.QueueBatchAsync(Batch(["u-1", "u-2", "u-3"]), TestContext.Current.CancellationToken);
        await fixture.Store.SetChoiceAsync("u-2", UserMailFixture.News, false, TestContext.Current.CancellationToken);

        await fixture.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);
        var delivery = await fixture.Queue.GetBatchAsync(queued.Id, TestContext.Current.CancellationToken);

        Assert.Equal(new UserMailOutcomes { Sent = 1, Skipped = 1, Failed = 1 }, delivery.Outcomes);
        Assert.Equal("u-3", Assert.Single(delivery.Failures).UserKey);
        Assert.NotNull(delivery.CompletedAt);
    }

    [Fact]
    public async Task AMailOfABatch_NamesItsBatch()
    {
        var fixture = WithUsers("u-1");
        var queued = await fixture.Queue.QueueBatchAsync(Batch(["u-1"]), TestContext.Current.CancellationToken);

        var delivery = await fixture.Queue.GetAsync(fixture.Outbox.Single.Id, TestContext.Current.CancellationToken);

        Assert.Equal(queued.Id, delivery.BatchId);
    }
}
