using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// A support case opened from a public form by somebody who is not signed in.
/// </summary>
/// <remarks>
/// <b>The case is opened like a mailed one from an unknown sender</b>: unassigned, never placed in a team on an
/// unverified address, and bound to the Email channel with the visitor as the correspondent, so an answer is mailed
/// to them.
/// </remarks>
public class ExternalSupportCaseTests
{
    private const string Visitor = "visitor@example.com";
    private const string Support = "support@example.net";
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ExternalSupportContact Contact() => new() { Name = "Vera Visitor", Email = Visitor };

    [Fact]
    public async Task ACase_OpensUnassigned_WithNoAuthorIdentity()
    {
        var (service, store, _, _) = Build();

        var raised = await service.RaiseExternalCaseAsync(Contact(), "Question", "Can you help?");

        Assert.Null(raised.TeamKey);
        Assert.Null(raised.AuthorIdentity);
        Assert.Equal("Vera Visitor", raised.AuthorName);
        Assert.Equal("Question", raised.Subject);
        Assert.Equal(SupportCaseStatus.Open, raised.Status);
        Assert.Equal(Now, raised.CreatedAt);
        await store.Received(1).AddCaseAsync(Arg.Is<SupportCase>(x => x.Id == raised.Id && x.TeamKey == null), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheOpeningMessage_IsTheVisitors_AndAlreadyDelivered()
    {
        var (service, store, _, _) = Build();

        await service.RaiseExternalCaseAsync(Contact(), "Question", "Can you help?");

        // Delivered and from the channel, so the reply path never mails the visitor their own message back.
        await store.Received(1).AddCaseAsync(Arg.Any<SupportCase>(),
            Arg.Is<SupportMessage>(x => x.Sequence == 1 && x.Kind == SupportMessageKind.User && x.Body == "Can you help?" && x.AuthorIdentity == null
                && x.AuthorName == "Vera Visitor" && x.Delivery == SupportMessageDelivery.Sent && x.Source == SupportChannelType.Email && x.SentAt == Now),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheCase_IsBoundToTheEmailChannel_WithTheVisitorAsTheCorrespondent()
    {
        var (service, _, _, _) = Build();

        var raised = await service.RaiseExternalCaseAsync(Contact(), "Question", "Can you help?");

        var binding = Assert.Single(raised.Bindings);
        Assert.Equal(SupportChannelType.Email, binding.ChannelType);
        Assert.Equal(Visitor, binding.Address);
        Assert.Equal(SupportCaseOptions.PrimaryMailboxName, binding.Mailbox);
        Assert.Equal(Support, binding.MailboxAddress);
        Assert.EndsWith("@example.net", binding.ExternalId);
        Assert.NotEqual(raised.Id, binding.ExternalId);
    }

    [Fact]
    public async Task ASupportAnswer_IsMailedToTheAddressTheVisitorGave()
    {
        var (service, store, _, _) = Build();
        var raised = await service.RaiseExternalCaseAsync(Contact(), "Question", "Can you help?");

        var mail = Substitute.For<IMailClient>();
        mail.CanSend.Returns(true);
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("answer-1@example.net"));
        store.GetCaseByBindingAsync(SupportChannelType.Email, raised.Bindings[0].ExternalId, Arg.Any<CancellationToken>()).Returns(raised);
        store.GetMessagesAsync(Arg.Any<string>(), raised.Id, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new SupportMessagePage { Items = [] });
        var channel = new EmailSupportChannel(mail, store, Options.Create(new MailOptions { FromAddress = Support }));

        var result = await channel.PostWithResultAsync(raised.Bindings[0], new SupportMessage { Sequence = 2, Kind = SupportMessageKind.User, AuthorIdentity = "staff-1", AuthorName = "Support", Body = "Yes.", SentAt = Now });

        Assert.True(result.Delivered);
        await mail.Received(1).SendAsync(Arg.Is<OutboundMail>(x => x.To == Visitor && x.Subject == "Re: Question"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNoSubject_OneIsDerivedFromTheMessage()
    {
        var (service, _, _, _) = Build();

        var raised = await service.RaiseExternalCaseAsync(Contact(), null, "The export is empty.");

        Assert.Equal("The export is empty.", raised.Subject);
    }

    [Fact]
    public async Task WithNoName_TheAddressNamesThem()
    {
        var (service, _, _, _) = Build();

        var raised = await service.RaiseExternalCaseAsync(new ExternalSupportContact { Email = Visitor }, "Question", "Hello");

        Assert.Equal(Visitor, raised.AuthorName);
    }

    [Fact]
    public async Task TheContact_IsKeptOnTheCase_TrimmedAndWithConsentAndHostFields()
    {
        var (service, _, _, _) = Build();
        var consent = Now.AddMinutes(-1);

        var raised = await service.RaiseExternalCaseAsync(new ExternalSupportContact
        {
            Name = "  Vera Visitor ",
            Email = $"  {Visitor} ",
            Phone = " 070-1234567 ",
            Organisation = " Acme AB ",
            ConsentGivenAt = consent,
            Fields = new Dictionary<string, string> { ["Product"] = "Export" }
        }, "Question", "Hello");

        var contact = raised.ExternalContact;
        Assert.Equal("Vera Visitor", contact.Name);
        Assert.Equal(Visitor, contact.Email);
        Assert.Equal("070-1234567", contact.Phone);
        Assert.Equal("Acme AB", contact.Organisation);
        Assert.Equal(consent, contact.ConsentGivenAt);
        Assert.Equal("Export", contact.Fields["Product"]);
        Assert.Equal(Visitor, raised.Bindings[0].Address);
    }

    [Fact]
    public async Task AnUnverifiedAddress_NeverPlacesTheCaseInATeam_ItOnlySuggests()
    {
        var team = new SupportCaseTeam { Key = "acme", Name = "Acme" };
        var directory = Substitute.For<ISupportSenderDirectory>();
        directory.FindAsync(Visitor, Arg.Any<CancellationToken>()).Returns([new SupportSender { Identity = "sub-1", Name = "Vera", Teams = [team] }]);
        var (service, _, _, _) = Build(directory: directory);

        var raised = await service.RaiseExternalCaseAsync(Contact(), "Question", "Hello");

        Assert.Null(raised.TeamKey);
        Assert.Null(raised.AuthorIdentity);
        Assert.Equal([team], raised.SuggestedTeams);
        Assert.Equal(SupportTeamSuggestionReason.SenderNotVerified, raised.SuggestionReason);
    }

    [Fact]
    public async Task ADirectoryThatFails_DoesNotLoseTheMessage()
    {
        var directory = Substitute.For<ISupportSenderDirectory>();
        directory.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<SupportSender[]>(_ => throw new InvalidOperationException("down"));
        var (service, store, _, _) = Build(directory: directory);

        var raised = await service.RaiseExternalCaseAsync(Contact(), "Question", "Hello");

        Assert.Empty(raised.SuggestedTeams);
        await store.Received(1).AddCaseAsync(Arg.Any<SupportCase>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheHost_IsToldACaseArrivedFromOutside_WithNoTeam()
    {
        var (service, _, notifier, _) = Build();

        var raised = await service.RaiseExternalCaseAsync(Contact(), "Question", "Hello");

        notifier.Received(1).Notify(Arg.Is<SupportCaseUpdatedEventArgs>(x => x.CaseId == raised.Id && x.TeamKey == null && x.Change == SupportCaseChange.Raised && x.FromChannel));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("two@@example.com")]
    [InlineData("a b@example.com")]
    [InlineData("visitor@example.com, other@example.com")]
    [InlineData("visitor@example.com\r\nBcc: someone@example.com")]
    public async Task AMissingOrMalformedAddress_IsRefused_AndNothingIsWritten(string address)
    {
        var (service, store, _, _) = Build();

        await Assert.ThrowsAsync<ArgumentException>(() => service.RaiseExternalCaseAsync(new ExternalSupportContact { Email = address }, "Question", "Hello"));

        await store.DidNotReceive().AddCaseAsync(Arg.Any<SupportCase>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public async Task AnEmptyMessage_IsRefused(string body)
    {
        var (service, store, _, _) = Build();

        await Assert.ThrowsAsync<ArgumentException>(() => service.RaiseExternalCaseAsync(Contact(), "Question", body));

        await store.DidNotReceive().AddCaseAsync(Arg.Any<SupportCase>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EveryFieldIsBounded_ByTheLimits()
    {
        var (service, store, _, _) = Build();
        var tooLong = (int length) => new string('x', length + 1);
        var tooMany = Enumerable.Range(0, SupportCaseLimits.MaxExternalFields + 1).ToDictionary(x => $"f{x}", _ => "v");

        await Assert.ThrowsAsync<ArgumentException>(() => service.RaiseExternalCaseAsync(Contact(), "Question", tooLong(SupportCaseLimits.MaxMessageLength)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RaiseExternalCaseAsync(Contact(), tooLong(SupportCaseLimits.MaxExternalSubjectLength), "Hello"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RaiseExternalCaseAsync(Contact() with { Name = tooLong(SupportCaseLimits.MaxExternalNameLength) }, "Question", "Hello"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RaiseExternalCaseAsync(Contact() with { Organisation = tooLong(SupportCaseLimits.MaxExternalNameLength) }, "Question", "Hello"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RaiseExternalCaseAsync(Contact() with { Phone = tooLong(SupportCaseLimits.MaxExternalPhoneLength) }, "Question", "Hello"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RaiseExternalCaseAsync(Contact() with { Email = $"{new string('x', SupportCaseLimits.MaxExternalEmailLength)}@example.com" }, "Question", "Hello"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RaiseExternalCaseAsync(Contact() with { Fields = tooMany }, "Question", "Hello"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RaiseExternalCaseAsync(Contact() with { Fields = new Dictionary<string, string> { [tooLong(SupportCaseLimits.MaxExternalFieldNameLength)] = "v" } }, "Question", "Hello"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RaiseExternalCaseAsync(Contact() with { Fields = new Dictionary<string, string> { ["k"] = tooLong(SupportCaseLimits.MaxExternalFieldValueLength) } }, "Question", "Hello"));

        await store.DidNotReceive().AddCaseAsync(Arg.Any<SupportCase>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheLimits_AreInclusive()
    {
        var (service, _, _, _) = Build();

        var raised = await service.RaiseExternalCaseAsync(
            Contact() with { Name = new string('n', SupportCaseLimits.MaxExternalNameLength), Phone = new string('1', SupportCaseLimits.MaxExternalPhoneLength) },
            new string('s', SupportCaseLimits.MaxExternalSubjectLength),
            new string('b', SupportCaseLimits.MaxMessageLength));

        Assert.NotNull(raised);
    }

    [Fact]
    public async Task NoReceipt_IsSent_ByDefault()
    {
        var (service, _, _, mail) = Build();

        await service.RaiseExternalCaseAsync(Contact(), "Question", "Hello");

        await mail.DidNotReceive().SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenTurnedOn_AReceiptIsMailedToTheVisitor_AndItsIdJoinsTheThread()
    {
        var (service, store, _, mail) = Build(receipt: true);
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("receipt-1@example.net"));

        var raised = await service.RaiseExternalCaseAsync(Contact(), "Question", "Hello");

        await mail.Received(1).SendAsync(Arg.Is<OutboundMail>(x => x.To == Visitor && x.Subject == "Re: Question"), Arg.Any<CancellationToken>());
        await store.Received(1).AddBindingAsync(null, raised.Id,
            Arg.Is<SupportChannelBinding>(x => x.ChannelType == SupportChannelType.Email && x.ExternalId == "receipt-1@example.net" && x.Address == Visitor), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AReceiptThatCannotBeSent_NeverLosesTheCase()
    {
        var (service, store, _, mail) = Build(receipt: true);
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns<MailSendResult>(_ => throw new InvalidOperationException("smtp down"));

        var raised = await service.RaiseExternalCaseAsync(Contact(), "Question", "Hello");

        Assert.NotNull(raised);
        await store.Received(1).AddCaseAsync(Arg.Any<SupportCase>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AReceipt_IsNotAttempted_WhenNoMailServerCanSend()
    {
        var (service, _, _, mail) = Build(receipt: true, canSend: false);

        await service.RaiseExternalCaseAsync(Contact(), "Question", "Hello");

        await mail.DidNotReceive().SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>());
    }

    private static (ExternalSupportCaseService Service, ISupportCaseStore Store, ISupportCaseNotifier Notifier, IMailClient Mail) Build(
        bool receipt = false, bool canSend = true, ISupportSenderDirectory directory = null)
    {
        var store = Substitute.For<ISupportCaseStore>();
        var notifier = Substitute.For<ISupportCaseNotifier>();
        var mail = Substitute.For<IMailClient>();
        mail.CanSend.Returns(canSend);
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("receipt@example.net"));

        var time = Substitute.For<TimeProvider>();
        time.GetUtcNow().Returns(new DateTimeOffset(Now));

        var service = new ExternalSupportCaseService(store, new SupportMailboxes(new MailOptions { FromAddress = Support }), time, notifier, directory, mail, new SupportExternalCaseOptions { SendReceipt = receipt });

        return (service, store, notifier, mail);
    }
}
