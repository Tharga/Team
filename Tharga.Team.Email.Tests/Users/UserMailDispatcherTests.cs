using MimeKit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Users;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>Sending queued mail: the recipient's choice and address read at send time, the footer and headers only on optional mail, retries with backoff, and one sender per mail however many instances run.</summary>
public class UserMailDispatcherTests
{
    private const string First = "instance-a";
    private const string Second = "instance-b";

    private readonly UserMailFixture _fixture = new();

    [Fact]
    public async Task AMail_IsSentToTheAddressOnTheUserRecord_FromTheConfiguredReplyAddress()
    {
        await _fixture.QueueAsync();

        var status = await _fixture.Dispatcher().SendNextAsync(First, default);

        var sent = Assert.Single(_fixture.Mail.Sent);
        Assert.Equal(UserMailStatus.Sent, status);
        Assert.Equal(UserMailFixture.Address, MailboxAddress.Parse(sent.To).Address);
        Assert.Equal("Anna Andersson", MailboxAddress.Parse(sent.To).Name);
        Assert.Equal("Hello", sent.Subject);
        Assert.Equal("support@example.com", sent.ReplyTo);

        var item = _fixture.Outbox.Single;
        Assert.Equal(UserMailStatus.Sent, item.Status);
        Assert.Equal("<m1@example.com>", item.MessageId);
        Assert.Equal(UserMailFixture.Start, item.CompletedAt);
        Assert.Equal(1, item.Attempts);
    }

    [Fact]
    public async Task TheAddressIsRead_WhenSent_NotWhenQueued()
    {
        await _fixture.QueueAsync();
        _fixture.Recipients.Add(new UserMailRecipient { Key = UserMailFixture.UserKey, EMail = "anna.new@example.org" });

        await _fixture.Dispatcher().SendNextAsync(First, default);

        Assert.Equal("anna.new@example.org", Assert.Single(_fixture.Mail.Sent).To);
    }

    [Fact]
    public async Task AReplyAddressOnTheMail_WinsOverTheConfiguredOne()
    {
        await _fixture.Queue.QueueAsync(new UserMail { RecipientUserKey = UserMailFixture.UserKey, Category = UserMailFixture.News, Subject = "S", Body = "B", ReplyTo = "team@example.com" });

        await _fixture.Dispatcher().SendNextAsync(First, default);

        Assert.Equal("team@example.com", Assert.Single(_fixture.Mail.Sent).ReplyTo);
    }

    [Fact]
    public async Task AnOptionalCategorysMail_CarriesTheUnsubscribeLink_InBothBodies_AndTheOneClickHeaders()
    {
        await _fixture.QueueAsync();

        await _fixture.Dispatcher().SendNextAsync(First, default);

        var sent = Assert.Single(_fixture.Mail.Sent);
        Assert.StartsWith("https://app.example.com/mail/unsubscribe?token=", sent.UnsubscribeUrl);
        Assert.Contains("You are receiving this because News is turned on in your mail settings.", sent.Body);
        Assert.Contains($"Unsubscribe: {sent.UnsubscribeUrl}", sent.Body);
        Assert.Contains($"<a href=\"{System.Net.WebUtility.HtmlEncode(sent.UnsubscribeUrl)}\">Unsubscribe</a>", sent.BodyHtml);

        var token = Uri.UnescapeDataString(sent.UnsubscribeUrl[(sent.UnsubscribeUrl.IndexOf("token=", StringComparison.Ordinal) + 6)..]);
        Assert.True(_fixture.Tokens.TryRead(token, out var user, out var category));
        Assert.Equal((UserMailFixture.UserKey, UserMailFixture.News), (user, category));

        var message = MailMessageFactory.Create(sent, _fixture.Options.Mail);
        Assert.Equal($"<{sent.UnsubscribeUrl}>", message.Headers[MailMessageFactory.ListUnsubscribeHeader]);
        Assert.Equal(MailMessageFactory.OneClickValue, message.Headers[MailMessageFactory.ListUnsubscribePostHeader]);
    }

    [Theory]
    [InlineData(UserMailCategories.AccountKey)]
    [InlineData(UserMailCategories.ServiceKey)]
    public async Task ARequiredCategorysMail_CarriesNeitherLinkNorHeader(string category)
    {
        await _fixture.QueueAsync(category);

        await _fixture.Dispatcher().SendNextAsync(First, default);

        var sent = Assert.Single(_fixture.Mail.Sent);
        Assert.Null(sent.UnsubscribeUrl);
        Assert.DoesNotContain("unsubscribe", sent.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unsubscribe", sent.BodyHtml, StringComparison.OrdinalIgnoreCase);

        var message = MailMessageFactory.Create(sent, _fixture.Options.Mail);
        Assert.False(message.Headers.Contains(MailMessageFactory.ListUnsubscribeHeader));
        Assert.False(message.Headers.Contains(MailMessageFactory.ListUnsubscribePostHeader));
    }

    [Fact]
    public async Task TheFooter_IsTranslatedThroughTheHostsTexts()
    {
        await _fixture.QueueAsync();
        var texts = new DictionaryTextResolver(new Dictionary<string, string>
        {
            [UserMailTexts.FooterReasonKey] = "Du får detta eftersom {0} är påslaget.",
            [UserMailTexts.FooterUnsubscribeKey] = "Avsluta",
            ["userMail.category.news.name"] = "Nyheter"
        });

        await _fixture.Dispatcher(texts).SendNextAsync(First, default);

        var sent = Assert.Single(_fixture.Mail.Sent);
        Assert.Contains("Du får detta eftersom Nyheter är påslaget.", sent.Body);
        Assert.Contains(">Avsluta</a>", sent.BodyHtml);
    }

    [Fact]
    public async Task OptingOutAfterQueuing_SkipsTheMail_AtSendTime_RatherThanFailingIt()
    {
        await _fixture.QueueAsync();
        await _fixture.Store.SetChoiceAsync(UserMailFixture.UserKey, UserMailFixture.News, false);

        var status = await _fixture.Dispatcher().SendNextAsync(First, default);

        Assert.Equal(UserMailStatus.Skipped, status);
        Assert.Empty(_fixture.Mail.Sent);
        Assert.Equal(UserMailStatus.Skipped, _fixture.Outbox.Single.Status);
        Assert.Equal(UserMailDispatcher.OptedOutReason, _fixture.Outbox.Single.Reason);
    }

    [Fact]
    public async Task ACategoryOffByDefault_IsSkipped_ForAUserWhoNeverTurnedItOn()
    {
        await _fixture.QueueAsync(UserMailFixture.Digest);

        Assert.Equal(UserMailStatus.Skipped, await _fixture.Dispatcher().SendNextAsync(First, default));
    }

    [Fact]
    public async Task ARequiredCategory_IsSentEvenToAUserWithAStoredOptOut()
    {
        await _fixture.QueueAsync(UserMailCategories.AccountKey);
        await _fixture.Store.SetChoiceAsync(UserMailFixture.UserKey, UserMailCategories.AccountKey, false);

        Assert.Equal(UserMailStatus.Sent, await _fixture.Dispatcher().SendNextAsync(First, default));
    }

    [Fact]
    public async Task AFailedSend_IsTriedAgain_AfterABackoffThatDoubles()
    {
        _fixture.Mail.Respond = _ => MailSendResult.Failed("451 try later");
        await _fixture.QueueAsync();
        var dispatcher = _fixture.Dispatcher();

        Assert.Equal(UserMailStatus.Queued, await dispatcher.SendNextAsync(First, default));
        var first = _fixture.Outbox.Single;
        Assert.Equal(UserMailFixture.Start.AddMinutes(1), first.NextAttemptAt);
        Assert.Equal("451 try later", first.Reason);
        Assert.Null(first.ClaimedBy);

        Assert.Null(await dispatcher.SendNextAsync(First, default));

        _fixture.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(UserMailStatus.Queued, await dispatcher.SendNextAsync(First, default));
        Assert.Equal(UserMailFixture.Start.AddMinutes(1 + 2), _fixture.Outbox.Single.NextAttemptAt);
        Assert.Equal(2, _fixture.Mail.Sent.Count);
    }

    [Fact]
    public async Task AMailThatKeepsFailing_IsGivenUp_AfterTheLastAttempt_WithTheReasonKept()
    {
        var fixture = new UserMailFixture(o => o.MaxAttempts = 3);
        fixture.Mail.Respond = _ => MailSendResult.Failed("550 mailbox unavailable");
        await fixture.QueueAsync(attachments: [new UserMailAttachment { FileName = "a.pdf", ContentType = "application/pdf", Content = [1, 2, 3] }]);
        var dispatcher = fixture.Dispatcher();

        for (var i = 0; i < 3; i++)
        {
            fixture.Time.Advance(TimeSpan.FromHours(1));
            await dispatcher.SendNextAsync(First, default);
        }

        var item = fixture.Outbox.Single;
        Assert.Equal(3, fixture.Mail.Sent.Count);
        Assert.Equal(UserMailStatus.Failed, item.Status);
        Assert.Equal("Gave up after 3 attempts. 550 mailbox unavailable", item.Reason);
        Assert.Null(item.Mail.Attachments.Single().Content);
        Assert.Equal(3, item.Mail.Attachments.Single().Size);

        fixture.Time.Advance(TimeSpan.FromDays(1));
        Assert.Null(await dispatcher.SendNextAsync(First, default));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(7, 60)]
    [InlineData(40, 60)]
    public void TheBackoff_DoublesFromTheRetryDelay_UpToTheMaximum(int attempts, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), UserMailDispatcher.Backoff(attempts, new UserMailOptions()));
    }

    [Fact]
    public async Task TwoSenders_NeverSendTheSameMail()
    {
        await _fixture.QueueAsync();
        await _fixture.QueueAsync();

        var claimedByFirst = await _fixture.Outbox.ClaimNextAsync(First, UserMailFixture.Start, TimeSpan.FromMinutes(5));
        await _fixture.Dispatcher().RunAsync(Second, default);

        Assert.Single(_fixture.Mail.Sent);
        Assert.Equal(UserMailStatus.Queued, (await _fixture.Outbox.GetAsync(claimedByFirst.Id)).Status);
        Assert.Equal(UserMailStatus.Sent, _fixture.Outbox.Items.Single(x => x.Id != claimedByFirst.Id).Status);
    }

    [Fact]
    public async Task ManySendersAtOnce_SendEachMailExactlyOnce()
    {
        for (var i = 0; i < 20; i++) await _fixture.QueueAsync();

        await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Task.Run(() => _fixture.Dispatcher().RunAsync($"instance-{i}", default))));

        Assert.Equal(20, _fixture.Mail.Sent.Count);
        Assert.All(_fixture.Outbox.Items, x => Assert.Equal(UserMailStatus.Sent, x.Status));
        Assert.Equal(20, _fixture.Outbox.Items.Select(x => x.MessageId).Distinct().Count());
    }

    [Fact]
    public async Task AClaimWhoseLeaseRanOut_IsTakenOver_AndTheFirstSendersLateOutcomeIsRefused()
    {
        await _fixture.QueueAsync();
        var stale = await _fixture.Outbox.ClaimNextAsync(First, UserMailFixture.Start, _fixture.Options.Lease);

        _fixture.Time.Advance(_fixture.Options.Lease);
        Assert.Equal(UserMailStatus.Sent, await _fixture.Dispatcher().SendNextAsync(Second, default));

        Assert.False(await _fixture.Outbox.MarkFailedAsync(stale.Id, First, "late", _fixture.Time.GetUtcNow().UtcDateTime));
        Assert.Equal(UserMailStatus.Sent, _fixture.Outbox.Single.Status);
        Assert.Equal(2, _fixture.Outbox.Single.Attempts);
    }

    [Fact]
    public async Task AUserWhoNoLongerExists_FailsAtOnce()
    {
        await _fixture.QueueAsync();
        _fixture.Recipients.Remove(UserMailFixture.UserKey);

        Assert.Equal(UserMailStatus.Failed, await _fixture.Dispatcher().SendNextAsync(First, default));
        Assert.Equal("No user has this key.", _fixture.Outbox.Single.Reason);
        Assert.Empty(_fixture.Mail.Sent);
    }

    [Fact]
    public async Task AUserRecordWithoutAnAddress_FailsAtOnce()
    {
        await _fixture.QueueAsync();
        _fixture.Recipients.Add(new UserMailRecipient { Key = UserMailFixture.UserKey });

        Assert.Equal(UserMailStatus.Failed, await _fixture.Dispatcher().SendNextAsync(First, default));
        Assert.Empty(_fixture.Mail.Sent);
    }

    [Fact]
    public async Task AStoreThatCannotBeRead_IsRetried_NotFailed()
    {
        await _fixture.QueueAsync();
        var recipients = Substitute.For<IUserMailRecipientDirectory>();
        recipients.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<UserMailRecipient>(_ => throw new TimeoutException("database unreachable"));
        var dispatcher = new UserMailDispatcher(_fixture.Outbox, _fixture.Categories, _fixture.Preferences, recipients, _fixture.Mail, _fixture.Policy,
            new UserMailFooter(_fixture.Tokens, _fixture.Wrapped, new UserMailText()), _fixture.Wrapped, _fixture.Time);

        Assert.Equal(UserMailStatus.Queued, await dispatcher.SendNextAsync(First, default));
        Assert.Equal("database unreachable", _fixture.Outbox.Single.Reason);
    }

    [Fact]
    public async Task OutsideProduction_TheMailGoesToTheOverrideAddress_WithTheRecipientInTheSubject()
    {
        _fixture.Policy = Policy(address: "test-inbox@example.com");
        await _fixture.QueueAsync();

        Assert.Equal(UserMailStatus.Sent, await _fixture.Dispatcher().SendNextAsync(First, default));

        var sent = Assert.Single(_fixture.Mail.Sent);
        Assert.Equal("test-inbox@example.com", sent.To);
        Assert.Equal($"[Staging -> {UserMailFixture.Address}] Hello", sent.Subject);
    }

    [Fact]
    public async Task OutsideProduction_ToAnAllowedDomain_TheMailGoesAsUsual()
    {
        _fixture.Policy = Policy(address: "test-inbox@example.com", allowed: "example.org");
        await _fixture.QueueAsync();

        await _fixture.Dispatcher().SendNextAsync(First, default);

        Assert.Equal(UserMailFixture.Address, MailboxAddress.Parse(Assert.Single(_fixture.Mail.Sent).To).Address);
    }

    [Fact]
    public async Task OutsideProduction_WithNoOverride_TheMailIsWithheld_AndRecordedAsFailedWithWhy()
    {
        _fixture.Policy = Policy(address: null);
        await _fixture.QueueAsync();

        Assert.Equal(UserMailStatus.Failed, await _fixture.Dispatcher().SendNextAsync(First, default));
        Assert.Empty(_fixture.Mail.Sent);
        Assert.Equal(UserMailDispatcher.WithheldReason, _fixture.Outbox.Single.Reason);
    }

    [Fact]
    public async Task AFileQueuedWithTheMail_IsSentWithIt()
    {
        await _fixture.QueueAsync(attachments: [new UserMailAttachment { FileName = "report.pdf", ContentType = "application/pdf", Content = [1, 2, 3] }]);

        await _fixture.Dispatcher().SendNextAsync(First, default);

        var file = Assert.Single(Assert.Single(_fixture.Mail.Sent).Attachments);
        Assert.Equal("report.pdf", file.FileName);
        Assert.Equal([1, 2, 3], file.Content);
    }

    [Fact]
    public async Task ABatch_StopsWhenNothingIsDue()
    {
        for (var i = 0; i < 3; i++) await _fixture.QueueAsync();

        Assert.Equal(3, await _fixture.Dispatcher().RunAsync(First, default));
        Assert.Equal(0, await _fixture.Dispatcher().RunAsync(First, default));
    }

    private static OutboundMailPolicy Policy(string address, string allowed = null)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Staging");

        return new OutboundMailPolicy(Options.Create(new OutboundMailPolicyOptions { Address = address, AllowedDomains = allowed == null ? [] : [allowed] }), environment);
    }
}
