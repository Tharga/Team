using System.Security.Claims;
using Microsoft.Extensions.Options;
using MimeKit;
using Tharga.Team.Email;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Service.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Answering a case raised in the app by mail, when its author cannot read answers on the site: the host says which address to write to, support
/// answers by mail from one of its addresses, and from then on the case is an ordinary mail thread.
/// </summary>
/// <remarks>Addresses are made up, on more than one domain; none is built into the package.</remarks>
public class SupportAnswerByMailTests
{
    private const string TeamKey = "team-a";
    private const string Main = "support@example.net";
    private const string Alias = "help@example.net";
    private const string Author = "owner@acme.example";
    private const string Staff = "staff-1";

    private readonly InMemorySupportCaseStore _store = new();
    private readonly IMailClient _mail = Substitute.For<IMailClient>();
    private readonly List<AuditEntry> _audit = [];
    private OutboundMail _sent;

    public SupportAnswerByMailTests()
    {
        _mail.CanSend.Returns(true);
        _mail.SendAsync(Arg.Do<OutboundMail>(x => _sent = x), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("answer-1@example.net"));
    }

    private ISupportCaseService Service(string address = Author, bool withResolver = true, bool withMail = true, ISupportCaseContactResolver resolver = null, IOutboundMailPolicy policy = null)
    {
        var options = new MailOptions { FromAddress = Main, FromName = "Example Support", PerCaseReplyTo = true, SendAs = [new MailSendAs { Address = Alias }] };
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Staff),
            new Claim(ClaimTypes.Name, "Anna Andersson"),
            new Claim(TeamClaimTypes.TeamKey, TeamKey),
            new Claim(TeamClaimTypes.SystemScope, SystemSupportScopes.AllManage)
        ], "test"))));

        var channel = new EmailSupportChannel(_mail, _store, Options.Create(options), policy: policy);
        var host = resolver ?? (withResolver ? new FixedResolver(address) : null);
        var service = new SupportCaseService(_store, authorizer, TimeProvider.System, withMail ? [channel] : [], mailboxes: new SupportMailboxes(options), contactResolver: host);

        return new AuditingSupportCaseServiceDecorator(
            new AuthorizationSupportCaseServiceDecorator(service, authorizer),
            new CompositeAuditLogger([new Collecting(_audit)], Options.Create(new AuditOptions())),
            new AuditEntryFactory(null));
    }

    private async Task<SupportCase> SiteCaseAsync(ISupportCaseService service)
        => await service.RaiseCaseAsync(TeamKey, "Please add system X", "Can you add it to the catalogue?");

    // ---- the offer

    [Fact]
    public async Task ASiteCaseWithAnAddressTheHostResolves_OffersToAnswerByMail_AndStillSaysItIsOnTheSite()
    {
        var service = Service();
        var raised = await SiteCaseAsync(service);

        var route = await service.GetAnswerRouteAsync(TeamKey, raised.Id);

        Assert.False(route.ByMail);
        Assert.NotNull(route.MailOffer);
        Assert.Equal(Author, route.MailOffer.To);
        Assert.Equal(Main, route.MailOffer.From);
        Assert.Equal([Main, Alias], route.MailOffer.Senders.Select(x => x.Address));
    }

    [Fact]
    public async Task ASiteCaseWithNoAddress_OffersNothing_AsBefore()
    {
        var service = Service(address: null);
        var raised = await SiteCaseAsync(service);

        var route = await service.GetAnswerRouteAsync(TeamKey, raised.Id);

        Assert.False(route.ByMail);
        Assert.Null(route.MailOffer);
    }

    [Fact]
    public async Task WithNoResolverRegistered_NothingIsOffered()
    {
        var service = Service(withResolver: false);
        var raised = await SiteCaseAsync(service);

        Assert.Null((await service.GetAnswerRouteAsync(TeamKey, raised.Id)).MailOffer);
    }

    [Fact]
    public async Task AResolverThatThrows_IsTreatedAsNoAddress()
    {
        var resolver = Substitute.For<ISupportCaseContactResolver>();
        resolver.GetMailAddressAsync(Arg.Any<SupportCase>(), Arg.Any<CancellationToken>()).Returns<Task<string>>(_ => throw new InvalidOperationException("the host directory is down"));
        var service = Service(resolver: resolver);
        var raised = await SiteCaseAsync(service);

        Assert.Null((await service.GetAnswerRouteAsync(TeamKey, raised.Id)).MailOffer);
    }

    [Theory]
    [InlineData("not an address")]
    [InlineData("a@b.example, c@d.example")]
    public async Task AnAddressThatIsNotOnePlainAddress_IsNeverOffered(string address)
    {
        var service = Service(address);
        var raised = await SiteCaseAsync(service);

        Assert.Null((await service.GetAnswerRouteAsync(TeamKey, raised.Id)).MailOffer);
    }

    [Fact]
    public async Task WithNoMailSetUp_NothingIsOffered()
    {
        var service = Service(withMail: false);
        var raised = await SiteCaseAsync(service);

        Assert.Null((await service.GetAnswerRouteAsync(TeamKey, raised.Id)).MailOffer);
    }

    [Fact]
    public async Task TheOutboundMailPolicy_ShowsOnTheOffer_AsOnAnyAnswer()
    {
        var policy = Substitute.For<IOutboundMailPolicy>();
        policy.Decide(Arg.Any<string>(), Arg.Any<string>()).Returns(new OutboundMailDecision(OutboundMailAction.Redirect, "test-inbox@example.org", "[Test] subject"));
        var service = Service(policy: policy);
        var raised = await SiteCaseAsync(service);

        var offer = (await service.GetAnswerRouteAsync(TeamKey, raised.Id)).MailOffer;

        Assert.Equal(MailPolicyEffect.Redirect, offer.Effect);
        Assert.Equal("test-inbox@example.org", offer.RedirectedTo);
    }

    // ---- the first mail answer

    [Fact]
    public async Task TheFirstMailAnswer_IsMailedToTheResolvedAddress_AndBindsTheCaseToMail()
    {
        var service = Service();
        var raised = await SiteCaseAsync(service);

        await service.AnswerCaseAsync(TeamKey, raised.Id, "Added.", null, [], new SupportAnswerChoice { ByMail = true });

        Assert.Equal(Author, _sent.To);
        Assert.StartsWith("Added.", _sent.Body);
        var route = await service.GetAnswerRouteAsync(TeamKey, raised.Id);
        Assert.True(route.ByMail);
        Assert.Equal(Author, route.To);
        Assert.Equal(Main, route.From);
        Assert.Equal("primary", route.Mailbox);
        Assert.Null(route.MailOffer);
    }

    [Fact]
    public async Task TheAnswerIsAlsoOnTheSite_AsToday()
    {
        var service = Service();
        var raised = await SiteCaseAsync(service);

        await service.AnswerCaseAsync(TeamKey, raised.Id, "Added.", null, [], new SupportAnswerChoice { ByMail = true });

        var messages = (await service.GetMessagesAsync(TeamKey, raised.Id)).Items;
        Assert.Contains(messages, x => x.AsSupport && x.Body == "Added.");
    }

    [Fact]
    public async Task TheChosenAddress_IsWhatItGoesFrom()
    {
        var service = Service();
        var raised = await SiteCaseAsync(service);

        await service.AnswerCaseAsync(TeamKey, raised.Id, "Added.", null, [], new SupportAnswerChoice { ByMail = true, SendFrom = Alias });

        Assert.Equal(Alias, MailboxAddress.Parse(_sent.From).Address);
        Assert.Equal(Alias, (await service.GetAnswerRouteAsync(TeamKey, raised.Id)).From);
    }

    [Fact]
    public async Task AnAnswerNotMarkedByMail_StaysOnTheSite_AndMailsNothing()
    {
        var service = Service();
        var raised = await SiteCaseAsync(service);

        await service.AnswerCaseAsync(TeamKey, raised.Id, "Added.", null, [], new SupportAnswerChoice());

        await _mail.DidNotReceiveWithAnyArgs().SendAsync(default, default);
        Assert.False((await service.GetAnswerRouteAsync(TeamKey, raised.Id)).ByMail);
    }

    [Fact]
    public async Task WithNoAddressToWriteTo_ByMailIsRefused_AndNothingIsWrittenOrSent()
    {
        var service = Service(address: null);
        var raised = await SiteCaseAsync(service);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnswerCaseAsync(TeamKey, raised.Id, "Added.", null, [], new SupportAnswerChoice { ByMail = true }));

        await _mail.DidNotReceiveWithAnyArgs().SendAsync(default, default);
        Assert.Single((await service.GetMessagesAsync(TeamKey, raised.Id)).Items);
        Assert.False((await service.GetAnswerRouteAsync(TeamKey, raised.Id)).ByMail);
    }

    [Fact]
    public async Task AnAddressNoMailboxAllows_IsRefused_BeforeTheCaseIsSwitched()
    {
        var service = Service();
        var raised = await SiteCaseAsync(service);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnswerCaseAsync(TeamKey, raised.Id, "Added.", null, [], new SupportAnswerChoice { ByMail = true, SendFrom = "stranger@example.org" }));

        Assert.False((await service.GetAnswerRouteAsync(TeamKey, raised.Id)).ByMail);
        await _mail.DidNotReceiveWithAnyArgs().SendAsync(default, default);
    }

    [Fact]
    public async Task AnAnswerAfterTheSwitch_IsAnOrdinaryMailAnswer_NoSecondSwitch()
    {
        var service = Service();
        var raised = await SiteCaseAsync(service);
        await service.AnswerCaseAsync(TeamKey, raised.Id, "Added.", null, [], new SupportAnswerChoice { ByMail = true });
        _audit.Clear();

        await service.AnswerCaseAsync(TeamKey, raised.Id, "Anything else?", null, [], new SupportAnswerChoice { ByMail = true });

        Assert.Equal(Author, _sent.To);
        Assert.DoesNotContain(_audit, x => x.Action == "switch-to-mail");
    }

    // ---- the thread

    [Fact]
    public async Task TheCustomersReply_ToTheMailsOwnId_FindsTheCase()
    {
        var service = Service();
        var raised = await SiteCaseAsync(service);
        await service.AnswerCaseAsync(TeamKey, raised.Id, "Added.", null, [], new SupportAnswerChoice { ByMail = true });

        var found = await _store.GetCaseByBindingAsync(SupportChannelType.Email, "answer-1@example.net");

        Assert.Equal(raised.Id, found?.Id);
    }

    [Fact]
    public async Task TheCustomersReply_ToThePerCaseAddress_FindsTheCase()
    {
        var service = Service();
        var raised = await SiteCaseAsync(service);
        await service.AnswerCaseAsync(TeamKey, raised.Id, "Added.", null, [], new SupportAnswerChoice { ByMail = true });

        var found = await CaseReplyAddress.FindByRecipientAsync(_store, [_sent.ReplyTo], new MailOptions { FromAddress = Main, PerCaseReplyTo = true }, TestContext.Current.CancellationToken);

        Assert.Equal(raised.Id, found?.Id);
    }

    // ---- the preview

    [Fact]
    public async Task ThePreviewOfTheFirstMailAnswer_ShowsTheMail_AndBindsNothing()
    {
        var service = Service();
        var raised = await SiteCaseAsync(service);

        var preview = await service.PreviewAnswerAsync(TeamKey, raised.Id, string.Empty, "<p>Added.</p>", new SupportAnswerChoice { ByMail = true });

        Assert.Equal(Author, preview.To);
        Assert.False((await service.GetAnswerRouteAsync(TeamKey, raised.Id)).ByMail);
        await _mail.DidNotReceiveWithAnyArgs().SendAsync(default, default);
    }

    [Fact]
    public async Task ThePreviewOfASiteAnswer_IsNone_AsBefore()
    {
        var service = Service();
        var raised = await SiteCaseAsync(service);

        Assert.Null(await service.PreviewAnswerAsync(TeamKey, raised.Id, string.Empty, "<p>Added.</p>", new SupportAnswerChoice()));
    }

    // ---- the audit

    [Fact]
    public async Task TheSwitch_IsAudited_WithTheCase_TheAddress_AndTheMailbox()
    {
        var service = Service();
        var raised = await SiteCaseAsync(service);

        await service.AnswerCaseAsync(TeamKey, raised.Id, "Added.", null, [], new SupportAnswerChoice { ByMail = true });

        var entry = Assert.Single(_audit, x => x.Action == "switch-to-mail");
        Assert.Equal(raised.Id, entry.Metadata[SupportAuditMetadataKeys.CaseId]);
        Assert.Equal(Author, entry.Metadata[SupportAuditMetadataKeys.MailAddress]);
        Assert.Equal("primary", entry.Metadata[SupportAuditMetadataKeys.Mailbox]);
        Assert.True(entry.Success);
    }

    // ---- bound when raised

    [Fact]
    public async Task ACaseRaisedWithAnAddress_IsBoundToMailAtOnce()
    {
        var service = Service(address: null);

        var raised = await service.RaiseCaseAsync(TeamKey, "Please add system X", "Can you add it?", new SupportRaiseOptions { MailAddress = Author });

        var route = await service.GetAnswerRouteAsync(TeamKey, raised.Id);
        Assert.True(route.ByMail);
        Assert.Equal(Author, route.To);
        Assert.Equal(Main, route.From);
        Assert.Null(route.MailOffer);
    }

    [Fact]
    public async Task ACaseRaisedWithAnAddress_IsAnsweredByMail_WithoutAnySwitch()
    {
        var service = Service(address: null);
        var raised = await service.RaiseCaseAsync(TeamKey, "Please add system X", "Can you add it?", new SupportRaiseOptions { MailAddress = Author });

        await service.AnswerCaseAsync(TeamKey, raised.Id, "Added.");

        Assert.Equal(Author, _sent.To);
        Assert.Equal("Re: Please add system X", _sent.Subject);
        Assert.DoesNotContain(_audit, x => x.Action == "switch-to-mail");
    }

    [Fact]
    public async Task TheRaiseIsAudited_WithTheAddressAndMailbox_ItWasBoundWith()
    {
        var service = Service(address: null);

        var raised = await service.RaiseCaseAsync(TeamKey, "Please add system X", "Can you add it?", new SupportRaiseOptions { MailAddress = Author });

        var entry = Assert.Single(_audit, x => x.Action == "raise");
        Assert.Equal(raised.Id, entry.Metadata[SupportAuditMetadataKeys.CaseId]);
        Assert.Equal(Author, entry.Metadata[SupportAuditMetadataKeys.MailAddress]);
        Assert.Equal("primary", entry.Metadata[SupportAuditMetadataKeys.Mailbox]);
    }

    [Fact]
    public async Task ACaseRaisedWithoutAnAddress_IsAsBefore()
    {
        var service = Service(address: null);

        var raised = await service.RaiseCaseAsync(TeamKey, "Please add system X", "Can you add it?", new SupportRaiseOptions());

        Assert.False((await service.GetAnswerRouteAsync(TeamKey, raised.Id)).ByMail);
        Assert.Empty(raised.Bindings);
    }

    [Fact]
    public async Task RaisingWithABadAddress_OrAnUnknownMailbox_OrNoMail_IsRefused_AndNothingIsWritten()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service().RaiseCaseAsync(TeamKey, "S", "B", new SupportRaiseOptions { MailAddress = "not an address" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().RaiseCaseAsync(TeamKey, "S", "B", new SupportRaiseOptions { MailAddress = Author, Mailbox = "no-such-box" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(withMail: false).RaiseCaseAsync(TeamKey, "S", "B", new SupportRaiseOptions { MailAddress = Author }));

        Assert.Empty((await Service().GetCasesAsync(TeamKey)).Items);
    }

    private sealed class FixedResolver(string address) : ISupportCaseContactResolver
    {
        public Task<string> GetMailAddressAsync(SupportCase supportCase, CancellationToken cancellationToken = default) => Task.FromResult(address);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }

    private sealed class Collecting(List<AuditEntry> entries) : IAuditLogger
    {
        public void Log(AuditEntry entry) => entries.Add(entry);

        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }
}
