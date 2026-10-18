using Tharga.Team.Email.Users;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>What the queue refuses, what it cleans up, and how a deduplication key makes queuing idempotent.</summary>
public class UserMailQueueTests
{
    private readonly UserMailFixture _fixture = new();

    private static UserMail Mail(string category = UserMailFixture.News) => new()
    {
        RecipientUserKey = UserMailFixture.UserKey,
        Category = category,
        Subject = "Hello",
        Body = "Text."
    };

    [Fact]
    public async Task AMail_IsStoredQueued_AndDueAtOnce()
    {
        var queued = await _fixture.Queue.QueueAsync(Mail());

        var item = _fixture.Outbox.Single;
        Assert.False(queued.Duplicate);
        Assert.Equal(queued.Id, item.Id);
        Assert.Equal(UserMailStatus.Queued, item.Status);
        Assert.Equal(UserMailFixture.Start, item.QueuedAt);
        Assert.Equal(UserMailFixture.Start, item.NextAttemptAt);
    }

    [Fact]
    public async Task AnUnknownCategory_IsRefused_WhenQueued()
    {
        var e = await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Queue.QueueAsync(Mail("nope")));

        Assert.Contains("'nope'", e.Message);
        Assert.Empty(_fixture.Outbox.Items);
    }

    [Fact]
    public async Task ABuiltInCategory_IsAccepted()
    {
        await _fixture.Queue.QueueAsync(Mail(UserMailCategories.AccountKey));

        Assert.Equal(UserMailCategories.AccountKey, _fixture.Outbox.Single.Mail.Category);
    }

    [Fact]
    public async Task TheHtml_IsSanitized_AndTheTextDerivedFromIt()
    {
        await _fixture.Queue.QueueAsync(Mail() with
        {
            Body = null,
            BodyHtml = "<p>Read <a href=\"https://example.com/news\">the news</a>.</p><script>alert(1)</script><img src=\"https://tracker.example.com/p.gif\" alt=\"\">"
        });

        var stored = _fixture.Outbox.Single.Mail;
        Assert.DoesNotContain("script", stored.BodyHtml);
        Assert.DoesNotContain("tracker", stored.BodyHtml);
        Assert.Contains("rel=\"noopener noreferrer\"", stored.BodyHtml);
        Assert.Equal("Read the news (https://example.com/news).", stored.Body);
    }

    [Fact]
    public async Task AGivenText_IsKeptBesideTheHtml()
    {
        await _fixture.Queue.QueueAsync(Mail() with { Body = "Own text.", BodyHtml = "<p>Html.</p>" });

        Assert.Equal("Own text.", _fixture.Outbox.Single.Mail.Body);
        Assert.Equal("<p>Html.</p>", _fixture.Outbox.Single.Mail.BodyHtml);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" ", "<p> </p>")]
    [InlineData(null, "<script>only()</script>")]
    public async Task AMailWithNothingToRead_IsRefused(string body, string html)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Queue.QueueAsync(Mail() with { Body = body, BodyHtml = html }));
    }

    [Fact]
    public async Task ASubjectOverSeveralLines_BecomesOne_AndAnEmptyOneIsRefused()
    {
        await _fixture.Queue.QueueAsync(Mail() with { Subject = "Hello\r\nBcc: someone@example.com" });

        Assert.Equal("Hello Bcc: someone@example.com", _fixture.Outbox.Single.Mail.Subject);
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Queue.QueueAsync(Mail() with { Subject = "\r\n" }));
    }

    [Fact]
    public async Task AReplyAddressThatIsNotOne_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Queue.QueueAsync(Mail() with { ReplyTo = "not an address" }));
    }

    [Fact]
    public async Task NoRecipient_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Queue.QueueAsync(Mail() with { RecipientUserKey = " " }));
    }

    [Fact]
    public async Task Files_OverTheCountOrSizeLimit_AreRefused_WithinThemAccepted()
    {
        var fixture = new UserMailFixture(o =>
        {
            o.MaxAttachmentCount = 2;
            o.MaxAttachmentBytes = 10;
        });
        UserMailAttachment File(int size) => new() { FileName = "a.pdf", ContentType = "application/pdf", Content = new byte[size] };

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.QueueAsync(attachments: [File(1), File(1), File(1)]));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.QueueAsync(attachments: [File(6), File(5)]));
        await fixture.QueueAsync(attachments: [File(6), File(4)]);

        Assert.Equal([6L, 4L], fixture.Outbox.Single.Mail.Attachments.Select(x => x.Size));
    }

    [Fact]
    public async Task AFileWithoutContent_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.QueueAsync(attachments: [new UserMailAttachment { FileName = "a.pdf", ContentType = "application/pdf" }]));
    }

    [Fact]
    public async Task ABodyOverTheLimit_IsRefused()
    {
        var fixture = new UserMailFixture(o => o.MaxBodyLength = 10);

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Queue.QueueAsync(Mail() with { Body = new string('x', 11) }));
    }

    [Fact]
    public async Task TheSameDeduplicationKey_QueuesOnce_AndAnswersWithTheFirst()
    {
        var first = await _fixture.QueueAsync(dedup: "welcome:user-1");
        var second = await _fixture.QueueAsync(dedup: " welcome:user-1 ");
        var other = await _fixture.QueueAsync(dedup: "welcome:user-2");

        Assert.False(first.Duplicate);
        Assert.True(second.Duplicate);
        Assert.Equal(first.Id, second.Id);
        Assert.False(other.Duplicate);
        Assert.Equal(2, _fixture.Outbox.Items.Count);
    }

    [Fact]
    public async Task WithoutADeduplicationKey_EveryMailIsQueued()
    {
        await _fixture.QueueAsync();
        await _fixture.QueueAsync();

        Assert.Equal(2, _fixture.Outbox.Items.Count);
    }

    [Fact]
    public async Task WhatBecameOfAMail_CanBeRead_WithoutItsContent()
    {
        var queued = await _fixture.QueueAsync(dedup: "d1");

        var delivery = await _fixture.Queue.GetAsync(queued.Id);

        Assert.Equal(UserMailStatus.Queued, delivery.Status);
        Assert.Equal(UserMailFixture.UserKey, delivery.RecipientUserKey);
        Assert.Equal(UserMailFixture.News, delivery.Category);
        Assert.Equal("Hello", delivery.Subject);
        Assert.Equal("d1", delivery.DeduplicationKey);
        Assert.Null(await _fixture.Queue.GetAsync("missing"));
    }
}
