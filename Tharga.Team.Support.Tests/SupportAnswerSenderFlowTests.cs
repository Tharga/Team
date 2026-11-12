using System.Security.Claims;
using Microsoft.Extensions.Options;
using MimeKit;
using Tharga.Team.Email;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Which address an answer goes from, and in whose name: the address the customer wrote to by default, the staff member's own when their role
/// and the mailbox allow it, never an address outside what the mailbox declared, and always a reply to a support address with the case's tracker.
/// </summary>
public class SupportAnswerSenderFlowTests
{
    private const string TeamKey = "team-a";
    private const string CaseId = "case-1";
    private const string Customer = "user@customer.example";
    private const string OpeningId = "opening-1@customer.example";
    private const string Main = "support@example.net";
    private const string Alias = "help@example.net";
    private const string Elsewhere = "info@example.org";
    private const string Staff = "anna@example.net";
    private const string Role = "Support";

    private static readonly DateTime Opened = new(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);

    private readonly InMemorySupportCaseStore _store = new();
    private readonly IMailClient _mail = Substitute.For<IMailClient>();
    private OutboundMail _sent;

    public SupportAnswerSenderFlowTests()
    {
        _mail.CanSend.Returns(true);
        _mail.SendAsync(Arg.Do<OutboundMail>(x => _sent = x), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("answer-1@example.net"));
    }

    [Fact]
    public async Task ACaseDeliveredToTheMainAddress_IsAnsweredAsItAlwaysWas()
    {
        await CaseAsync(Main);

        await Service(Mailbox()).AnswerCaseAsync(TeamKey, CaseId, "Hi.");

        Assert.Null(_sent.From);
        Assert.Null(_sent.ReplyTo);
    }

    [Fact]
    public async Task ACaseDeliveredToASendAsAddress_IsAnsweredFromIt_ByDefault()
    {
        await CaseAsync(Alias);

        await Service(Mailbox(sendAs: [Alias])).AnswerCaseAsync(TeamKey, CaseId, "Hi.");

        Assert.Equal(Alias, MailboxAddress.Parse(_sent.From).Address);
        Assert.Equal("Example Support", MailboxAddress.Parse(_sent.From).Name);
    }

    [Fact]
    public async Task ACaseDeliveredToAnAddressTheMailboxCannotSendAs_IsAnsweredFromTheMailboxAddress()
    {
        await CaseAsync("unlisted@example.com");

        await Service(Mailbox(sendAs: [Alias])).AnswerCaseAsync(TeamKey, CaseId, "Hi.");

        Assert.Null(_sent.From);
    }

    [Fact]
    public async Task ASendAsAddressOnAnotherDomain_Works_AndItsNameCanDifferFromTheMailboxs()
    {
        await CaseAsync(Elsewhere);
        var options = Mailbox();
        options.SendAs = [new MailSendAs { Address = Elsewhere, Name = "Example Info" }];

        await Service(options).AnswerCaseAsync(TeamKey, CaseId, "Hi.");

        Assert.Equal((Elsewhere, "Example Info"), (MailboxAddress.Parse(_sent.From).Address, MailboxAddress.Parse(_sent.From).Name));
    }

    [Fact]
    public async Task TheChosenAddress_IsUsed_AndStoredOnTheEntry()
    {
        await CaseAsync(Main);

        await Service(Mailbox(sendAs: [Alias])).AnswerCaseAsync(TeamKey, CaseId, "Hi.", null, [], new SupportAnswerChoice { SendFrom = Alias });

        Assert.Equal(Alias, MailboxAddress.Parse(_sent.From).Address);
        Assert.Equal(Alias, (await Last()).SentFrom);
    }

    [Fact]
    public async Task AnAddressInNoMailboxsList_IsRefused_AndNothingIsSentOrStored()
    {
        await CaseAsync(Main);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(Mailbox(sendAs: [Alias])).AnswerCaseAsync(TeamKey, CaseId, "Hi.", null, [], new SupportAnswerChoice { SendFrom = "stranger@example.com" }));

        await _mail.DidNotReceiveWithAnyArgs().SendAsync(default, default);
        Assert.Single((await _store.GetMessagesAsync(TeamKey, CaseId, null, 50)).Items);
    }

    [Fact]
    public async Task TheRoute_ListsEveryAddressThatMayBeChosen_WithTheDefaultFirstInEffect()
    {
        await CaseAsync(Alias);

        var route = await Service(Mailbox(sendAs: [Alias, Elsewhere])).GetAnswerRouteAsync(TeamKey, CaseId);

        Assert.Equal(Alias, route.From);
        Assert.Equal([Main, Alias, Elsewhere], route.Senders.Select(x => x.Address));
        Assert.All(route.Senders, x => Assert.False(x.Personal));
    }

    [Fact]
    public async Task AnAnswerFromASendAsAddress_HasAReplyAddressOnThatAddress_ThatReadsBackToTheCase()
    {
        await CaseAsync(Alias, trackingToken: "tracking-abc123");
        var options = Mailbox(sendAs: [Alias]);
        options.PerCaseReplyTo = true;

        await Service(options).AnswerCaseAsync(TeamKey, CaseId, "Hi.");

        Assert.Equal("help+tracking-abc123@example.net", _sent.ReplyTo);

        var found = await CaseReplyAddress.FindByRecipientAsync(_store, ["help+tracking-abc123@example.net"], options, TestContext.Current.CancellationToken);
        Assert.Equal(CaseId, found?.Id);
    }

    [Fact]
    public async Task AStaffMemberInTheRole_AnswersFromTheirOwnAddressAndName_WhenTheMailboxDeclaresTheDomain()
    {
        await CaseAsync(Main);

        await Service(Mailbox(domains: ["example.net"]), Answers(), Anna()).AnswerCaseAsync(TeamKey, CaseId, "Hi.");

        Assert.Equal((Staff, "Anna Andersson"), (MailboxAddress.Parse(_sent.From).Address, MailboxAddress.Parse(_sent.From).Name));
        Assert.Equal(Staff, (await Last()).SentFrom);
    }

    [Fact]
    public async Task TheReplyOfAStaffAnswer_GoesToTheSupportAddressWithTheTracker_NeverToTheStaffAddress()
    {
        await CaseAsync(Alias, trackingToken: "tracking-abc123");
        var options = Mailbox(sendAs: [Alias], domains: ["example.net"]);
        options.PerCaseReplyTo = true;

        await Service(options, Answers(), Anna()).AnswerCaseAsync(TeamKey, CaseId, "Hi.");

        Assert.Equal(Staff, MailboxAddress.Parse(_sent.From).Address);
        Assert.Equal("help+tracking-abc123@example.net", _sent.ReplyTo);
    }

    [Fact]
    public async Task WithoutPerCaseReplyAddresses_ThePersonalAnswersReplyStillGoesToTheSupportAddress()
    {
        await CaseAsync(Alias);

        await Service(Mailbox(sendAs: [Alias], domains: ["example.net"]), Answers(), Anna()).AnswerCaseAsync(TeamKey, CaseId, "Hi.");

        Assert.Equal(Alias, _sent.ReplyTo);
    }

    [Fact]
    public async Task AStaffAddressOnADomainTheMailboxDidNotDeclare_FallsBackToTheAddressTheCustomerWroteTo()
    {
        await CaseAsync(Alias);

        await Service(Mailbox(sendAs: [Alias]), Answers(), Anna()).AnswerCaseAsync(TeamKey, CaseId, "Hi.");

        Assert.Equal(Alias, MailboxAddress.Parse(_sent.From).Address);
    }

    [Fact]
    public async Task AStaffMemberOutsideTheRole_AnswersFromSupport_AndCannotChooseTheirOwnAddress()
    {
        await CaseAsync(Main);
        var service = Service(Mailbox(domains: ["example.net"]), Answers(), Anna(roles: []));

        await service.AnswerCaseAsync(TeamKey, CaseId, "Hi.");
        Assert.Null(_sent.From);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnswerCaseAsync(TeamKey, CaseId, "Again.", null, [], new SupportAnswerChoice { SendFrom = Staff }));
    }

    [Fact]
    public async Task WithNoRoleConfigured_NobodyAnswersFromTheirOwnAddress()
    {
        await CaseAsync(Main);

        await Service(Mailbox(domains: ["example.net"]), new SupportAnswerOptions(), Anna()).AnswerCaseAsync(TeamKey, CaseId, "Hi.");

        Assert.Null(_sent.From);
    }

    [Fact]
    public async Task AStaffMemberMayStillPickASupportAddress_InsteadOfTheirOwn()
    {
        await CaseAsync(Main);

        await Service(Mailbox(sendAs: [Alias], domains: ["example.net"]), Answers(), Anna()).AnswerCaseAsync(TeamKey, CaseId, "Hi.", null, [], new SupportAnswerChoice { SendFrom = Alias });

        Assert.Equal(Alias, MailboxAddress.Parse(_sent.From).Address);
    }

    [Fact]
    public async Task AnEntryNobodyChoseAnAddressFor_GoesFromTheDefault_NotFromAPersonalAddress()
    {
        await CaseAsync(Main);
        var channel = Channel(Mailbox(domains: ["example.net"]), Answers());
        var binding = (await _store.GetCaseAsync(TeamKey, CaseId)).Bindings[0];

        await channel.PostWithResultAsync(binding, new SupportMessage { Sequence = 2, Kind = SupportMessageKind.Assistant, AuthorName = "Helper", Body = "Hello.", SentAt = Opened });

        Assert.Null(_sent.From);
    }

    [Fact]
    public async Task APersonalAddressNoMailboxCoversAnyMore_FallsBackWhenSentAgain()
    {
        await CaseAsync(Main);
        var channel = Channel(Mailbox(), Answers());
        var binding = (await _store.GetCaseAsync(TeamKey, CaseId)).Bindings[0];

        await channel.PostWithResultAsync(binding, new SupportMessage { Sequence = 2, Kind = SupportMessageKind.User, AuthorName = "Anna Andersson", Body = "Hello.", SentAt = Opened, AsSupport = true, SentFrom = Staff });

        Assert.Null(_sent.From);
    }

    [Fact]
    public async Task AResendOfAStaffAnswer_StillGoesFromTheStaffAddress_WithoutTheStaffMemberPresent()
    {
        await CaseAsync(Main);
        var channel = Channel(Mailbox(domains: ["example.net"]), Answers());
        var binding = (await _store.GetCaseAsync(TeamKey, CaseId)).Bindings[0];

        await channel.PostWithResultAsync(binding, new SupportMessage { Sequence = 2, Kind = SupportMessageKind.User, AuthorName = "Anna Andersson", Body = "Hello.", SentAt = Opened, AsSupport = true, SentFrom = Staff });

        Assert.Equal((Staff, "Anna Andersson"), (MailboxAddress.Parse(_sent.From).Address, MailboxAddress.Parse(_sent.From).Name));
    }

    [Fact]
    public async Task AServerRefusingTheSender_FailsTheAnswerWithTheReason_RatherThanSendingFromAnotherAddress()
    {
        await CaseAsync(Main);
        _mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.SenderNotAccepted("550 not allowed"));
        var channel = Channel(Mailbox(domains: ["example.net"]), Answers());
        var binding = (await _store.GetCaseAsync(TeamKey, CaseId)).Bindings[0];

        var result = await channel.PostWithResultAsync(binding, new SupportMessage { Sequence = 2, Kind = SupportMessageKind.User, AuthorName = "Anna", Body = "Hello.", SentAt = Opened, AsSupport = true, SentFrom = Staff });

        Assert.False(result.Delivered);
        Assert.Contains(Staff, result.Error);
        await _mail.Received(1).SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThePreview_ShowsTheFromAndTheReplyToTheAnswerWillUse()
    {
        await CaseAsync(Alias, trackingToken: "tracking-abc123");
        var options = Mailbox(sendAs: [Alias], domains: ["example.net"]);
        options.PerCaseReplyTo = true;

        var preview = await Service(options, Answers(), Anna()).PreviewAnswerAsync(TeamKey, CaseId, "Hi.", null);
        await Service(options, Answers(), Anna()).AnswerCaseAsync(TeamKey, CaseId, "Hi.");

        Assert.Equal(("Anna Andersson", Staff), (MailboxAddress.Parse(preview.From).Name, MailboxAddress.Parse(preview.From).Address));
        Assert.Equal(_sent.ReplyTo, preview.ReplyTo);
        Assert.Equal(_sent.From, preview.From);
    }

    [Fact]
    public async Task TheRoute_OffersTheStaffMembersOwnAddress_AndMakesItTheDefault()
    {
        await CaseAsync(Main);

        var route = await Service(Mailbox(domains: ["example.net"]), Answers(), Anna()).GetAnswerRouteAsync(TeamKey, CaseId);

        Assert.Equal(Staff, route.From);
        Assert.Contains(route.Senders, x => x.Personal && x.Address == Staff);
    }

    [Fact]
    public async Task WithTheOptionOff_TheSignatureIsExactlyAsBefore()
    {
        await CaseAsync(Main);

        await Service(Mailbox(), new SupportAnswerOptions { Signature = ["Example Support"] }, Anna()).AnswerCaseAsync(TeamKey, CaseId, "Hi.");

        Assert.StartsWith("Hi.\n\nExample Support\n\nOn ", _sent.Body);
    }

    [Fact]
    public async Task WithTheOptionOn_TheAnswersNameGoesFirstInTheSignature_InTheTextAndTheHtml()
    {
        await CaseAsync(Main);
        var answers = new SupportAnswerOptions { NameTheAnswerer = true, Signature = ["Example Support"], Closing = "Best wishes,{newline}{Name}" };

        await Service(Mailbox(), answers, Anna()).AnswerCaseAsync(TeamKey, CaseId, string.Empty, "<p>Hi <b>there</b>.</p>", []);

        Assert.StartsWith("Hi there.\n\nBest wishes,\nAnna Andersson\nExample Support\n\nOn ", _sent.Body);
        Assert.Equal("<p>Hi <b>there</b>.</p><p class=\"signature\">Best wishes,<br>Anna Andersson<br>Example Support</p><p>On 2026-10-17 08:00 UTC, A Customer wrote:</p><blockquote>The export is empty.</blockquote>", _sent.BodyHtml);
    }

    [Fact]
    public async Task TheDefaultClosing_IsEnglish_WhenNoneIsConfigured()
    {
        await CaseAsync(Main);

        await Service(Mailbox(), new SupportAnswerOptions { NameTheAnswerer = true }, Anna()).AnswerCaseAsync(TeamKey, CaseId, "Hi.");

        Assert.StartsWith("Hi.\n\nKind regards,\nAnna Andersson\n\nOn ", _sent.Body);
    }

    [Fact]
    public async Task UntickingTheName_ForOneAnswer_LeavesItOutOfThatAnswerOnly()
    {
        await CaseAsync(Main);
        var service = Service(Mailbox(), new SupportAnswerOptions { NameTheAnswerer = true, Signature = ["Example Support"] }, Anna());

        await service.AnswerCaseAsync(TeamKey, CaseId, "One.", null, [], new SupportAnswerChoice { SignWithName = false });
        var without = _sent.Body;
        await service.AnswerCaseAsync(TeamKey, CaseId, "Two.");

        Assert.DoesNotContain("Anna Andersson\nExample Support", without);
        Assert.StartsWith("One.\n\nExample Support", without);
        Assert.Contains("Kind regards,\nAnna Andersson\nExample Support", _sent.Body);
    }

    [Fact]
    public async Task TheNameCanBeTurnedOnForOneAnswer_WhenTheOptionIsOff()
    {
        await CaseAsync(Main);

        await Service(Mailbox(), new SupportAnswerOptions(), Anna()).AnswerCaseAsync(TeamKey, CaseId, "Hi.", null, [], new SupportAnswerChoice { SignWithName = true });

        Assert.StartsWith("Hi.\n\nKind regards,\nAnna Andersson", _sent.Body);
    }

    [Fact]
    public async Task AnEarlierSupportEntrySignedWithAName_IsQuotedUnderThatName_AndOneSignedWithoutUnderSupportsName()
    {
        await CaseAsync(Main);
        var service = Service(Mailbox(), new SupportAnswerOptions { SupportName = "Example Support" }, Anna());

        await service.AnswerCaseAsync(TeamKey, CaseId, "Named.", null, [], new SupportAnswerChoice { SignWithName = true });
        await service.AnswerCaseAsync(TeamKey, CaseId, "Unnamed.", null, [], new SupportAnswerChoice { SignWithName = false });
        await service.AnswerCaseAsync(TeamKey, CaseId, "Third.");

        Assert.Contains(", Anna Andersson wrote:\n> Named.", _sent.Body);
        Assert.Contains(", Example Support wrote:\n> Unnamed.", _sent.Body);
    }

    private async Task<SupportMessage> Last()
    {
        var messages = (await _store.GetMessagesAsync(TeamKey, CaseId, null, 50)).Items;

        return messages.OrderBy(x => x.Sequence).Last();
    }

    private async Task CaseAsync(string deliveredTo, string trackingToken = null)
        => await _store.AddCaseAsync(
            new SupportCase
            {
                Id = CaseId,
                TeamKey = TeamKey,
                AuthorIdentity = null,
                AuthorName = "A Customer",
                Subject = "Export is empty",
                Status = SupportCaseStatus.Open,
                CreatedAt = Opened,
                MessageCount = 1,
                TrackingToken = trackingToken,
                Bindings = [new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = OpeningId, Address = Customer, MailboxAddress = deliveredTo }]
            },
            new SupportMessage { Sequence = 1, Kind = SupportMessageKind.User, AuthorName = "A Customer", Body = "The export is empty.", SentAt = Opened, Source = SupportChannelType.Email, Delivery = SupportMessageDelivery.Sent });

    private static MailOptions Mailbox(string[] sendAs = null, string[] domains = null)
        => new() { FromAddress = Main, FromName = "Example Support", SendAs = [.. (sendAs ?? []).Select(x => new MailSendAs { Address = x })], SendAsDomains = domains ?? [] };

    private static SupportAnswerOptions Answers() => new() { StaffSenderRole = Role };

    private EmailSupportChannel Channel(MailOptions options, SupportAnswerOptions answers = null)
        => new(_mail, _store, Options.Create(options), answers: answers);

    private ISupportCaseService Service(MailOptions options, SupportAnswerOptions answers = null, TeamAuthorizer authorizer = null)
    {
        authorizer ??= Anna();
        var channel = Channel(options, answers);

        return new AuthorizationSupportCaseServiceDecorator(new SupportCaseService(_store, authorizer, TimeProvider.System, [channel], answerOptions: answers), authorizer);
    }

    private static TeamAuthorizer Anna(string[] roles = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "anna"),
            new(ClaimTypes.Name, "Anna Andersson"),
            new(ClaimTypes.Email, Staff),
            new(TeamClaimTypes.SystemScope, SystemSupportScopes.AllManage)
        };

        claims.AddRange((roles ?? [Role]).Select(x => new Claim(ClaimTypes.Role, x)));

        return new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
