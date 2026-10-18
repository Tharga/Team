using Tharga.Team.Email.Users;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>
/// Contact mail goes through the queue of mail to users and its own way out of it: to the address it was queued with,
/// unless that address opted out of the category or of all contact mail by then, from the contact sender with a footer
/// saying who sends it, why it came and how to stop it, and the unsubscribe headers; never in the name of mail to users.
/// </summary>
public class ContactMailSendingTests
{
    private static async Task<UserMailItem> SendAsync(ContactMailFixture fixture, UserMailDispatcher dispatcher = null)
    {
        await (dispatcher ?? fixture.Dispatcher()).RunAsync("me", TestContext.Current.CancellationToken);
        return fixture.Mail.Outbox.Items.Single();
    }

    [Fact]
    public async Task AContactMail_GoesToItsAddress_ThroughTheContactTransport_AndIsRecordedSent()
    {
        var fixture = new ContactMailFixture();
        await fixture.QueueAsync();

        var item = await SendAsync(fixture);

        var sent = Assert.Single(fixture.ContactMail.Sent);
        Assert.Empty(fixture.Mail.Mail.Sent);
        Assert.Equal("\"Kim Customer\" <kim@customer.example>", sent.To);
        Assert.Equal("Something for you", sent.Subject);
        Assert.Equal(UserMailStatus.Sent, item.Status);
        Assert.Equal(ContactMailFixture.Address, item.RecipientAddress);
        Assert.NotNull(item.MessageId);
    }

    [Fact]
    public async Task TheFooter_SaysWhoSendsIt_WhyItCame_AndHowToStopIt_InBothBodies()
    {
        var fixture = new ContactMailFixture();
        await fixture.QueueAsync();

        await SendAsync(fixture);

        var sent = fixture.ContactMail.Sent.Single();
        Assert.Contains($"Sent by {ContactMailFixture.SenderLine}.", sent.Body);
        Assert.Contains("You receive this because Example thinks it may be of interest to you.", sent.Body);
        Assert.Contains($"Unsubscribe: {sent.UnsubscribeUrl}", sent.Body);
        Assert.Contains($"<p>Sent by {ContactMailFixture.SenderLine}.</p>", sent.BodyHtml);
        Assert.Contains("<a href=\"https://app.example.com/mail/unsubscribe?token=", sent.BodyHtml);
        Assert.StartsWith("https://app.example.com/mail/unsubscribe?token=", sent.UnsubscribeUrl);
    }

    [Fact]
    public async Task TheLink_NamesTheAddressAndTheCategory_AndNoUser()
    {
        var fixture = new ContactMailFixture();
        await fixture.QueueAsync();

        await SendAsync(fixture);

        var token = Uri.UnescapeDataString(fixture.ContactMail.Sent.Single().UnsubscribeUrl.Split("token=")[1]);
        Assert.True(fixture.Mail.Tokens.TryReadAddress(token, out var address, out var category));
        Assert.Equal((ContactMailFixture.Address, ContactMailFixture.Offers), (address, category));
        Assert.False(fixture.Mail.Tokens.TryRead(token, out _, out _));
    }

    [Fact]
    public async Task WithoutASenderLine_TheFooterNamesTheProduct()
    {
        var fixture = new ContactMailFixture(o => o.SenderLine = null, m => m.ProductName = "Example Cloud");
        await fixture.QueueAsync();

        await SendAsync(fixture);

        Assert.Contains("Sent by Example Cloud.", fixture.ContactMail.Sent.Single().Body);
    }

    [Fact]
    public async Task ASenderLineOverSeveralLines_KeepsItsBreaks_InTheHtml()
    {
        var fixture = new ContactMailFixture(o => o.SenderLine = "Example Ltd\r\n1 Example Street");
        await fixture.QueueAsync();

        await SendAsync(fixture);

        Assert.Contains("<p>Sent by Example Ltd<br>1 Example Street.</p>", fixture.ContactMail.Sent.Single().BodyHtml);
    }

    [Fact]
    public async Task TheFooter_IsTranslatedThroughTheHostsTexts()
    {
        var fixture = new ContactMailFixture();
        await fixture.QueueAsync();
        var texts = new DictionaryTextResolver(new Dictionary<string, string>
        {
            [ContactMailTexts.FooterSenderKey] = "Skickat av {0}.",
            [ContactMailTexts.FooterReasonKey] = "Du får det här eftersom {0} tror att det kan intressera dig.",
            [UserMailTexts.FooterUnsubscribeKey] = "Avregistrera"
        });

        await SendAsync(fixture, fixture.Dispatcher(texts));

        var body = fixture.ContactMail.Sent.Single().Body;
        Assert.Contains($"Skickat av {ContactMailFixture.SenderLine}.", body);
        Assert.Contains("Du får det här eftersom Example tror att det kan intressera dig.", body);
        Assert.Contains("Avregistrera: https://", body);
    }

    [Fact]
    public async Task RepliesGoWhereTheContactOptionsSay_ElseWhereMailToUsersSays()
    {
        var own = new ContactMailFixture(o => o.ReplyTo = "sales@example.com");
        await own.QueueAsync();
        var inherited = new ContactMailFixture();
        await inherited.QueueAsync();

        await SendAsync(own);
        await SendAsync(inherited);

        Assert.Equal("sales@example.com", own.ContactMail.Sent.Single().ReplyTo);
        Assert.Equal("support@example.com", inherited.ContactMail.Sent.Single().ReplyTo);
    }

    [Fact]
    public async Task AnAddressThatOptedOutOfTheCategory_IsSkipped_AndNothingIsSent()
    {
        var fixture = new ContactMailFixture();
        await fixture.QueueAsync();
        await fixture.OptOutAsync(ContactMailFixture.Offers, " KIM@customer.example ");

        var item = await SendAsync(fixture);

        Assert.Empty(fixture.ContactMail.Sent);
        Assert.Equal(UserMailStatus.Skipped, item.Status);
        Assert.Equal(UserMailDispatcher.ContactOptedOutReason, item.Reason);
    }

    [Fact]
    public async Task AnAddressThatOptedOutOfEverything_IsSkipped_InEveryCategory()
    {
        var fixture = new ContactMailFixture();
        await fixture.QueueAsync(ContactMailFixture.Events);
        await fixture.OptOutAsync(null);

        var item = await SendAsync(fixture);

        Assert.Empty(fixture.ContactMail.Sent);
        Assert.Equal(UserMailStatus.Skipped, item.Status);
        Assert.Equal(UserMailDispatcher.ContactOptedOutOfAllReason, item.Reason);
    }

    [Fact]
    public async Task AnOptOutOfAnotherCategory_OrOfAnotherAddress_StopsNothing()
    {
        var fixture = new ContactMailFixture();
        await fixture.QueueAsync();
        await fixture.OptOutAsync(ContactMailFixture.Events);
        await fixture.OptOutAsync(null, "someone@customer.example");

        var item = await SendAsync(fixture);

        Assert.Equal(UserMailStatus.Sent, item.Status);
        Assert.Single(fixture.ContactMail.Sent);
    }

    [Fact]
    public async Task WithoutContactMailRegistered_AContactMailFails_AndIsNotSentAsMailToUsers()
    {
        var fixture = new ContactMailFixture();
        await fixture.QueueAsync();

        var item = await SendAsync(fixture, fixture.Mail.Dispatcher());

        Assert.Equal(UserMailStatus.Failed, item.Status);
        Assert.Equal(UserMailDispatcher.ContactsNotRegisteredReason, item.Reason);
        Assert.Empty(fixture.Mail.Mail.Sent);
    }

    [Fact]
    public async Task ACategoryNoLongerRegistered_Fails_WithTheReason()
    {
        var fixture = new ContactMailFixture();
        await fixture.QueueAsync("retired");

        var item = await SendAsync(fixture);

        Assert.Equal(UserMailStatus.Failed, item.Status);
        Assert.Contains("'retired'", item.Reason);
    }

    [Fact]
    public async Task OutsideProduction_AWithheldContactMail_FailsAtOnce_AndARedirectedOneGoesToTheOverride()
    {
        var withheld = new ContactMailFixture();
        withheld.Mail.Policy.Decide(Arg.Any<string>(), Arg.Any<string>()).Returns(new OutboundMailDecision(OutboundMailAction.Withhold, null, null));
        await withheld.QueueAsync();
        var redirected = new ContactMailFixture();
        redirected.Mail.Policy.Decide(Arg.Any<string>(), Arg.Any<string>()).Returns(x => new OutboundMailDecision(OutboundMailAction.Redirect, "tester@example.com", $"[test] {x.ArgAt<string>(1)}"));
        await redirected.QueueAsync();

        Assert.Equal(UserMailStatus.Failed, (await SendAsync(withheld)).Status);
        Assert.Empty(withheld.ContactMail.Sent);

        await SendAsync(redirected);
        var sent = redirected.ContactMail.Sent.Single();
        Assert.Equal("tester@example.com", sent.To);
        Assert.Equal("[test] Something for you", sent.Subject);
    }

    [Fact]
    public async Task AFailedSend_IsTriedAgainLater_AsMailToUsersIs()
    {
        var fixture = new ContactMailFixture();
        fixture.ContactMail.Respond = _ => MailSendResult.Failed("Server busy");
        await fixture.QueueAsync();

        var item = await SendAsync(fixture);

        Assert.Equal(UserMailStatus.Queued, item.Status);
        Assert.Equal("Server busy", item.Reason);
        Assert.True(item.NextAttemptAt > UserMailFixture.Start);
    }

    [Fact]
    public async Task WithATemplate_TheContactFooterGoesInItsFooterPlaceholder()
    {
        var fixture = new ContactMailFixture();
        fixture.Mail.Template = new MailTemplate(Microsoft.Extensions.Options.Options.Create(new MailTemplateOptions { Html = "<main>{{content}}</main><footer>{{footer}}</footer>" }));
        await fixture.QueueAsync();

        await SendAsync(fixture);

        var html = fixture.ContactMail.Sent.Single().BodyHtml;
        Assert.StartsWith("<main><p>An <b>offer</b>.</p></main><footer><p>Sent by", html);
        Assert.Contains("Unsubscribe</a></p></footer>", html);
    }

    [Fact]
    public void TheContactTransport_SendsFromTheContactSender_ThroughTheSmtpServerOfMailToUsers()
    {
        var mail = new UserMailOptions();
        mail.Mail.FromAddress = "hello@example.com";
        mail.Mail.FromName = "Example";
        mail.Mail.Smtp.Host = "smtp.example.com";
        mail.Mail.Smtp.Port = 2525;
        mail.Mail.Smtp.UserName = "user";
        mail.Mail.Smtp.Password = "secret";

        var own = ContactMailSender.MailOptions(mail, new ContactMessageOptions { FromAddress = "sales@example.com", FromName = "Example Sales" });
        var inherited = ContactMailSender.MailOptions(mail, new ContactMessageOptions());

        Assert.Equal(("sales@example.com", "Example Sales"), (own.FromAddress, own.FromName));
        Assert.Equal(("smtp.example.com", 2525, "user", "secret"), (own.Smtp.Host, own.Smtp.Port, own.Smtp.UserName, own.Smtp.Password));
        Assert.Equal(("hello@example.com", "Example"), (inherited.FromAddress, inherited.FromName));
        Assert.Equal("\"Example Sales\" <sales@example.com>", ContactMailSender.Display(mail, new ContactMessageOptions { FromAddress = "sales@example.com", FromName = "Example Sales" }));
    }

    [Fact]
    public async Task ABounceOnAContactMail_MarksItBounced()
    {
        var fixture = new ContactMailFixture();
        await fixture.QueueAsync();
        var sent = await SendAsync(fixture);

        var recognised = await new UserMailBounceHandler(fixture.Mail.Outbox).HandleAsync(new MailBounce { OriginalMessageId = sent.MessageId, Reason = "550 No such user", ReceivedAt = UserMailFixture.Start.AddHours(1) }, TestContext.Current.CancellationToken);

        Assert.True(recognised);
        var item = fixture.Mail.Outbox.Items.Single();
        Assert.Equal(UserMailStatus.Bounced, item.Status);
        Assert.Equal("550 No such user", item.Reason);
    }

    [Fact]
    public async Task ContactMail_IsNotPartOfTheHistoryOfAUserWhoHoldsTheAddress()
    {
        var fixture = new ContactMailFixture();
        await fixture.QueueAsync(ContactMailFixture.Offers, UserMailFixture.Address);
        await SendAsync(fixture);

        var history = await fixture.Mail.Queue.ListForRecipientAsync(UserMailFixture.UserKey, UserMailFixture.Address, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(history.Items);
    }

    [Fact]
    public async Task HowOftenAContactCategoryIsSent_IsCountedPerAddress_LeavingMailToUsersOut()
    {
        var fixture = new ContactMailFixture();
        await fixture.QueueAsync(ContactMailFixture.Offers, "a@customer.example", "b@customer.example");
        await fixture.QueueAsync(ContactMailFixture.Offers, "a@customer.example");
        await fixture.Mail.QueueAsync();
        await fixture.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);

        var offers = await fixture.Frequencies.GetAsync(ContactMailFixture.Offers, TestContext.Current.CancellationToken);
        var events = await fixture.Frequencies.GetAsync(ContactMailFixture.Events, TestContext.Current.CancellationToken);

        Assert.Equal((3, 2), (offers.Sent, offers.Recipients));
        Assert.Equal(0, events.Sent);
        Assert.Null(await fixture.Frequencies.GetAsync(UserMailFixture.News, TestContext.Current.CancellationToken));
    }
}
