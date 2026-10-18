using Tharga.Team.Email.Users;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>
/// Staff write to the contacts a host lists: each distinct address once, in the order first listed, the host's references
/// kept with it; those that opted out of the category or of all contact mail, those that are no address and those the host
/// left out are named and not queued; the content is stored once; what became of every address is read back with the
/// references; and staff can opt an address out. Sending, reading and opting out are audited, never with an address.
/// </summary>
public class ContactMessageServiceTests
{
    private readonly ContactMessageFixture _fixture = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ContactAddressee[] Mixed() =>
    [
        ContactMessageFixture.Row("Kim@Customer.example", "contact-1", "Kim Customer", "Customer Ltd"),
        ContactMessageFixture.Row("lee@customer.example", "contact-2", null, "Customer Ltd"),
        ContactMessageFixture.Row(" kim@customer.example ", "contact-3", "Kim C."),
        ContactMessageFixture.Row("opted@customer.example", "contact-4"),
        ContactMessageFixture.Row("not an address", "contact-5"),
        ContactMessageFixture.Row("excluded@customer.example", "contact-6", excluded: "Already a customer")
    ];

    [Fact]
    public async Task TheChoices_AreTheContactCategories_AndTheLimits()
    {
        var choices = await _fixture.Service.GetChoicesAsync(Ct);

        Assert.Equal([ContactMailFixture.Offers, ContactMailFixture.Events], choices.Categories.Select(x => x.Key));
        Assert.Equal((10, 10L * 1024 * 1024, 200_000), (choices.Limits.MaxAttachmentCount, choices.Limits.MaxAttachmentBytes, choices.Limits.MaxBodyLength));
        Assert.Equal(10_000, choices.MaxAddressees);
    }

    [Fact]
    public async Task TheList_HoldsEachAddressOnce_InTheOrderFirstListed_WithEveryRowsReference()
    {
        var list = await _fixture.Service.GetAddresseesAsync(Mixed(), ContactMailFixture.Offers, Ct);

        Assert.Equal(["kim@customer.example", "lee@customer.example", "opted@customer.example", "not an address", "excluded@customer.example"], list.Items.Select(x => x.Address));
        var kim = list.Items[0];
        Assert.Equal(["contact-1", "contact-3"], kim.References);
        Assert.Equal(("Kim Customer", "Customer Ltd"), (kim.Name, kim.Detail));
        Assert.Equal(ContactAddresseeStatus.Reached, kim.Status);
    }

    [Fact]
    public async Task AnAddressThatOptedOut_IsMarked_OfTheCategoryOrOfEverything()
    {
        await _fixture.Contacts.OptOutAsync(ContactMailFixture.Offers, "opted@customer.example");
        await _fixture.Contacts.OptOutAsync(null, "lee@customer.example");
        await _fixture.Contacts.OptOutAsync(ContactMailFixture.Events, "kim@customer.example");

        var list = await _fixture.Service.GetAddresseesAsync(Mixed(), ContactMailFixture.Offers, Ct);

        Assert.Equal((ContactAddresseeStatus.OptedOut, false), (list.Items[2].Status, list.Items[2].OptedOutOfAll));
        Assert.Equal((ContactAddresseeStatus.OptedOut, true), (list.Items[1].Status, list.Items[1].OptedOutOfAll));
        Assert.Equal(ContactAddresseeStatus.Reached, list.Items[0].Status);
        Assert.Equal((1, 2, 2), (list.Reached, list.OptedOut, list.LeftOut));
    }

    [Fact]
    public async Task NoAddress_AndTheHostsOwnReason_AreLeftOut_AndSaySo()
    {
        var list = await _fixture.Service.GetAddresseesAsync(Mixed(), ContactMailFixture.Offers, Ct);

        var invalid = list.Items.Single(x => x.Address == "not an address");
        Assert.Equal((ContactAddresseeStatus.LeftOut, true, null), (invalid.Status, invalid.InvalidAddress, invalid.ExcludedReason));
        var excluded = list.Items.Single(x => x.Address == "excluded@customer.example");
        Assert.Equal((ContactAddresseeStatus.LeftOut, false, "Already a customer"), (excluded.Status, excluded.InvalidAddress, excluded.ExcludedReason));
        Assert.Equal(["contact-6"], excluded.References);
    }

    [Fact]
    public async Task AnAddressSomeRowMails_IsReached_WithTheReferencesOfTheRowsThatMailIt()
    {
        ContactAddressee[] rows =
        [
            ContactMessageFixture.Row("kim@customer.example", "old-record", "Kim Old", excluded: "Duplicate record"),
            ContactMessageFixture.Row("KIM@customer.example", "new-record", "Kim New")
        ];

        var kim = Assert.Single((await _fixture.Service.GetAddresseesAsync(rows, ContactMailFixture.Offers, Ct)).Items);

        Assert.Equal(ContactAddresseeStatus.Reached, kim.Status);
        Assert.Equal(["new-record"], kim.References);
        Assert.Equal("Kim New", kim.Name);
    }

    [Fact]
    public async Task AListLongerThanAllowed_OrACategoryThatIsNotAContactOne_IsRefused()
    {
        var fixture = new ContactMessageFixture(o => o.MaxAddressees = 2);

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.GetAddresseesAsync(Mixed(), ContactMailFixture.Offers, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SendAsync(ContactMessageFixture.Message(Mixed()), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Service.GetAddresseesAsync(Mixed(), UserMailFixture.News, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Service.SendAsync(ContactMessageFixture.Message(Mixed(), UserMailCategories.ServiceKey), Ct));
        Assert.Empty(fixture.Mail.Outbox.Items);
    }

    [Fact]
    public async Task Sending_QueuesOneMailPerAddressReached_AsContactMail_AndStoresTheContentOnce()
    {
        await _fixture.Contacts.OptOutAsync(ContactMailFixture.Offers, "opted@customer.example");
        var file = new UserMailAttachment { FileName = "offer.pdf", ContentType = "application/pdf", Content = [1, 2, 3] };

        var sent = await _fixture.Service.SendAsync(ContactMessageFixture.Message(Mixed(), attachments: [file]), Ct);

        var items = _fixture.Mail.Outbox.Items;
        Assert.Equal(["kim@customer.example", "lee@customer.example"], items.Select(x => x.Mail.RecipientAddress));
        Assert.Equal(["Kim Customer", null], items.Select(x => x.Mail.RecipientName));
        Assert.All(items, x =>
        {
            Assert.Null(x.Mail.RecipientUserKey);
            Assert.Equal(UserMailKind.Contact, x.Mail.Kind);
            Assert.Equal(sent.Id, x.BatchId);
        });
        var batch = Assert.Single(_fixture.Mail.Outbox.Batches).Value;
        Assert.Equal(("Something for you", "<p>An <b>offer</b>.</p>", "An offer."), (batch.Subject, batch.BodyHtml, batch.Body));
        Assert.Equal([1, 2, 3], Assert.Single(batch.Attachments).Content);
        Assert.Equal((2, 1, 2, 1), (sent.RecipientCount, sent.OptedOutCount, sent.LeftOutCount, sent.AttachmentCount));
        Assert.Equal(2, sent.Outcomes.Queued);
    }

    [Fact]
    public async Task Sending_ReturnsEveryAddressListed_WithWhereItStands_AndTheHostsReferences()
    {
        await _fixture.Contacts.OptOutAsync(null, "opted@customer.example");

        var sent = await _fixture.Service.SendAsync(ContactMessageFixture.Message(Mixed()), Ct);

        Assert.Equal(5, sent.Recipients.Length);
        Assert.Equal((ContactRecipientStatus.Queued, "contact-1,contact-3"), (sent.Recipients[0].Status, string.Join(',', sent.Recipients[0].References)));
        Assert.Equal((ContactRecipientStatus.OptedOut, true), (sent.Recipients[2].Status, sent.Recipients[2].OptedOutOfAll));
        Assert.Equal((ContactRecipientStatus.LeftOut, true), (sent.Recipients[3].Status, sent.Recipients[3].InvalidAddress));
        Assert.Equal((ContactRecipientStatus.LeftOut, "Already a customer"), (sent.Recipients[4].Status, sent.Recipients[4].Reason));
        Assert.All(sent.Recipients, x => Assert.Equal(UserMailFixture.Start, x.ChangedAt));
        Assert.Equal(("Sam Staff", ContactMessageFixture.StaffKey), (sent.SentByName, sent.SentByUserKey));
    }

    [Fact]
    public async Task Sending_RecordsTheMessageAndItsList_InTheLog()
    {
        var sent = await _fixture.Service.SendAsync(ContactMessageFixture.Message(Mixed()), Ct);

        var entry = Assert.Single(_fixture.Log.Entries);
        Assert.Equal((sent.Id, ContactMailFixture.Offers, "Something for you", UserMailFixture.Start), (entry.Id, entry.Category, entry.Subject, entry.SentAt));
        Assert.Equal(5, entry.Addressees.Length);
    }

    [Fact]
    public async Task AListNobodyOnCanBeMailed_IsRefused_AndNothingIsQueuedOrLogged()
    {
        await _fixture.Contacts.OptOutAsync(null, "kim@customer.example");

        var e = await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Service.SendAsync(ContactMessageFixture.Message([ContactMessageFixture.Row("kim@customer.example"), ContactMessageFixture.Row("nope")]), Ct));

        Assert.Contains("Nobody", e.Message);
        Assert.Empty(_fixture.Mail.Outbox.Items);
        Assert.Empty(_fixture.Log.Entries);
        var entry = Assert.Single(_fixture.Mail.Audit.Entries);
        Assert.False(entry.Success);
    }

    [Fact]
    public async Task AMessageWithNoSubject_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Service.SendAsync(ContactMessageFixture.Message(Mixed()) with { Subject = " " }, Ct));

        Assert.Empty(_fixture.Mail.Outbox.Items);
    }

    [Fact]
    public async Task Sending_IsAudited_WithCounts_AndNeverAnAddress()
    {
        await _fixture.Service.SendAsync(ContactMessageFixture.Message(Mixed()), Ct);

        var entry = Assert.Single(_fixture.Mail.Audit.Entries);
        Assert.Equal((ContactMessageAudit.Feature, ContactMessageAudit.SendAction, true), (entry.Feature, entry.Action, entry.Success));
        Assert.Equal("3", entry.Metadata[ContactMessageAudit.Recipients]);
        Assert.Equal("2", entry.Metadata[ContactMessageAudit.LeftOut]);
        Assert.DoesNotContain(entry.Metadata.Values, x => x.Contains('@'));
    }

    [Fact]
    public async Task AnAddressThatOptsOutAfterTheMessageIsQueued_IsStillNotMailed()
    {
        var sent = await _fixture.Service.SendAsync(ContactMessageFixture.Message([ContactMessageFixture.Row("kim@customer.example", "c-1"), ContactMessageFixture.Row("lee@customer.example", "c-2")]), Ct);
        await _fixture.Service.SuppressAsync("lee@customer.example", cancellationToken: Ct);

        await _fixture.Contacts.Dispatcher().RunAsync("me", Ct);

        Assert.Equal(["kim@customer.example"], _fixture.Contacts.ContactMail.Sent.Select(x => x.To));
        var recipients = await _fixture.Service.GetRecipientsAsync(sent.Id, cancellationToken: Ct);
        var lee = recipients.Items.Single(x => x.Address == "lee@customer.example");
        Assert.Equal((ContactRecipientStatus.OptedOut, true, null), (lee.Status, lee.OptedOutOfAll, lee.Reason));
        Assert.Equal(["c-2"], lee.References);
    }

    [Fact]
    public async Task ThePreview_IsTheMailAContactReceives_FromTheContactSender_InTheTemplate_WithTheFooter()
    {
        var fixture = new ContactMessageFixture(o =>
        {
            o.FromAddress = "sales@example.com";
            o.FromName = "Example Sales";
        });
        fixture.Mail.Template = new MailTemplate(Microsoft.Extensions.Options.Options.Create(new MailTemplateOptions { Html = "<main>{{content}}</main><footer>{{footer}}</footer>" }));

        var preview = await fixture.Service.PreviewMailAsync(ContactMessageFixture.Message([]), Ct);

        Assert.Equal("\"Example Sales\" <sales@example.com>", preview.From);
        Assert.Equal("support@example.com", preview.ReplyTo);
        Assert.Null(preview.To);
        Assert.Equal("Something for you", preview.Subject);
        Assert.StartsWith("<main><p>An <b>offer</b>.</p></main><footer><p>Sent by Example Ltd", preview.Html);
        Assert.Contains("?token=preview", preview.Html);
        Assert.Contains("You receive this because Example Sales thinks it may be of interest to you.", preview.Text);
        Assert.Empty(fixture.Mail.Outbox.Items);
        Assert.Empty(fixture.Mail.Audit.Entries);
    }

    [Fact]
    public async Task ThePreview_RefusesACategoryThatIsNotAContactOne()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Service.PreviewMailAsync(ContactMessageFixture.Message([], UserMailFixture.News), Ct));
    }

    [Fact]
    public async Task TheLog_ListsSentMessagesNewestFirst_WithLiveCounts()
    {
        var first = await _fixture.Service.SendAsync(ContactMessageFixture.Message([ContactMessageFixture.Row("kim@customer.example")]), Ct);
        _fixture.Mail.Time.Advance(TimeSpan.FromMinutes(1));
        var second = await _fixture.Service.SendAsync(ContactMessageFixture.Message([ContactMessageFixture.Row("lee@customer.example"), ContactMessageFixture.Row("max@customer.example")]), Ct);
        await _fixture.Contacts.Dispatcher().RunAsync("me", Ct);

        var page = await _fixture.Service.GetSentAsync(cancellationToken: Ct);

        Assert.Equal([second.Id, first.Id], page.Items.Select(x => x.Id));
        Assert.Equal(2, page.Items[0].Outcomes.Sent);
        Assert.Empty(page.Items[0].Recipients);
        var one = await _fixture.Service.GetSentMessageAsync(first.Id, Ct);
        Assert.Equal((1, 0), (one.Outcomes.Sent, one.Outcomes.Queued));
        Assert.Null(await _fixture.Service.GetSentMessageAsync("unknown", Ct));
    }

    [Fact]
    public async Task TheRecipients_AreThoseQueuedThenThoseNeverQueued_EachWithWhatBecameOfIt()
    {
        await _fixture.Contacts.OptOutAsync(ContactMailFixture.Offers, "opted@customer.example");
        var sent = await _fixture.Service.SendAsync(ContactMessageFixture.Message(Mixed()), Ct);
        await _fixture.Contacts.Dispatcher().RunAsync("me", Ct);

        var page = await _fixture.Service.GetRecipientsAsync(sent.Id, cancellationToken: Ct);

        Assert.Equal(["kim@customer.example", "lee@customer.example", "opted@customer.example", "not an address", "excluded@customer.example"], page.Items.Select(x => x.Address));
        Assert.Equal([ContactRecipientStatus.Sent, ContactRecipientStatus.Sent, ContactRecipientStatus.OptedOut, ContactRecipientStatus.LeftOut, ContactRecipientStatus.LeftOut], page.Items.Select(x => x.Status));
        Assert.Equal(["contact-1", "contact-3"], page.Items[0].References);
        Assert.Equal(("Kim Customer", "Customer Ltd"), (page.Items[0].Name, page.Items[0].Detail));
        Assert.NotNull(page.Items[0].MessageId);
        Assert.Null(page.NextCursor);
    }

    [Theory]
    [InlineData(ContactRecipientStatus.Sent, new[] { "kim@customer.example", "lee@customer.example" })]
    [InlineData(ContactRecipientStatus.OptedOut, new[] { "opted@customer.example" })]
    [InlineData(ContactRecipientStatus.LeftOut, new[] { "not an address", "excluded@customer.example" })]
    [InlineData(ContactRecipientStatus.Failed, new string[0])]
    public async Task TheRecipients_CanBeLimitedToOneStatus(ContactRecipientStatus status, string[] expected)
    {
        await _fixture.Contacts.OptOutAsync(ContactMailFixture.Offers, "opted@customer.example");
        var sent = await _fixture.Service.SendAsync(ContactMessageFixture.Message(Mixed()), Ct);
        await _fixture.Contacts.Dispatcher().RunAsync("me", Ct);

        var page = await _fixture.Service.GetRecipientsAsync(sent.Id, status, cancellationToken: Ct);

        Assert.Equal(expected, page.Items.Select(x => x.Address));
    }

    [Fact]
    public async Task TheRecipients_ArePagedAcrossTheQueuedAndTheNeverQueued()
    {
        var sent = await _fixture.Service.SendAsync(ContactMessageFixture.Message(Mixed()), Ct);
        var seen = new List<string>();
        string cursor = null;

        do
        {
            var page = await _fixture.Service.GetRecipientsAsync(sent.Id, null, cursor, 2, Ct);
            Assert.True(page.Items.Length <= 2);
            seen.AddRange(page.Items.Select(x => x.Address));
            cursor = page.NextCursor;
        } while (cursor != null);

        Assert.Equal(["kim@customer.example", "lee@customer.example", "opted@customer.example", "not an address", "excluded@customer.example"], seen);
    }

    [Fact]
    public async Task ACursorNotIssued_IsRefused_AndAnUnknownMessageHasNoRecipients()
    {
        var sent = await _fixture.Service.SendAsync(ContactMessageFixture.Message(Mixed()), Ct);

        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Service.GetRecipientsAsync(sent.Id, null, "x:1", 10, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Service.GetRecipientsAsync(sent.Id, null, "l:-1", 10, Ct));
        Assert.Null(await _fixture.Service.GetRecipientsAsync("unknown", cancellationToken: Ct));
    }

    [Fact]
    public async Task ReadingTheLog_IsAudited_WithCounts()
    {
        var sent = await _fixture.Service.SendAsync(ContactMessageFixture.Message(Mixed()), Ct);

        await _fixture.Service.GetSentAsync(cancellationToken: Ct);
        await _fixture.Service.GetSentMessageAsync(sent.Id, Ct);
        await _fixture.Service.GetRecipientsAsync(sent.Id, ContactRecipientStatus.Queued, cancellationToken: Ct);

        Assert.Equal([ContactMessageAudit.SendAction, ContactMessageAudit.LogAction, ContactMessageAudit.OpenAction, ContactMessageAudit.RecipientsAction], _fixture.Mail.Audit.Entries.Select(x => x.Action));
        var recipients = _fixture.Mail.Audit.Entries[^1];
        Assert.Equal(("Queued", "3"), (recipients.Metadata[ContactMessageAudit.Status], recipients.Metadata[ContactMessageAudit.Count]));
    }

    [Fact]
    public async Task StaffCanOptAnAddressOut_OfACategoryOrOfEverything_OnceEach()
    {
        Assert.True(await _fixture.Service.SuppressAsync(" Kim@Customer.example ", ContactMailFixture.Offers, Ct));
        Assert.False(await _fixture.Service.SuppressAsync("kim@customer.example", ContactMailFixture.Offers, Ct));
        Assert.True(await _fixture.Service.SuppressAsync("kim@customer.example", cancellationToken: Ct));

        Assert.Equal([ContactMailFixture.Offers, null], _fixture.Contacts.Suppressions.Items.Select(x => x.Category));
        Assert.All(_fixture.Contacts.Suppressions.Items, x =>
        {
            Assert.Equal("kim@customer.example", x.Address);
            Assert.Equal(ContactMailSuppressionSource.Staff, x.Source);
        });
        Assert.Equal(2, _fixture.Mail.Audit.Entries.Count);
        Assert.All(_fixture.Mail.Audit.Entries, x =>
        {
            Assert.Equal((ContactMessageAudit.SuppressAction, AuditEventType.DataChange), (x.Action, x.EventType));
            Assert.Equal("Staff", x.Metadata[ContactMessageAudit.Source]);
            Assert.DoesNotContain(x.Metadata.Values, v => v.Contains('@'));
        });
        Assert.Equal(ContactMessageAudit.EveryCategory, _fixture.Mail.Audit.Entries[1].Metadata[ContactMessageAudit.Category]);
    }

    [Fact]
    public async Task OptingOutSomethingThatIsNotAnAddress_OrInACategoryThatIsNotAContactOne_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Service.SuppressAsync("not an address", cancellationToken: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Service.SuppressAsync("kim@customer.example", UserMailFixture.News, Ct));

        Assert.Empty(_fixture.Contacts.Suppressions.Items);
    }

    [Fact]
    public async Task AMessageThatCannotBeLogged_IsStillQueued_AndTheFailureSurfaces()
    {
        _fixture.Log.FailWith = new InvalidOperationException("Store down");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.Service.SendAsync(ContactMessageFixture.Message(Mixed()), Ct));

        Assert.Equal(3, _fixture.Mail.Outbox.Items.Count);
    }
}
