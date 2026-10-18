using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MimeKit;
using Tharga.Team.Email;
using Tharga.Team.Service;
using Tharga.Team.Service.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Beside the answer box: where the answer goes — the address it is mailed to and the support address it is sent
/// from, what this environment's outbound policy does with it, or that it is read on the site — and a preview of the
/// mail as the person who raised the case would receive it, composed exactly as the sent answer is.
/// </summary>
public class SupportAnswerRouteTests
{
    private const string TeamKey = "team-a";
    private const string CaseId = "case-1";
    private const string Customer = "user@customer.example";
    private const string OpeningId = "opening-1@customer.example";
    private const string Override = "testers@example.net";
    private const string Hosted = "hosted";

    private static readonly DateTime Opened = new(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);

    private readonly InMemorySupportCaseStore _store = new();
    private readonly IMailClient _mail = Substitute.For<IMailClient>();
    private readonly IOutboundMailPolicy _policy = Substitute.For<IOutboundMailPolicy>();

    public SupportAnswerRouteTests()
    {
        _mail.CanSend.Returns(true);
        _mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("answer-1@example.net"));
        Decide(OutboundMailAction.Deliver);
    }

    [Fact]
    public async Task InProduction_TheRouteNamesTheAddressAndTheMailbox_AndNothingElse()
    {
        await MailedCaseAsync();

        var route = await Staff().GetAnswerRouteAsync(TeamKey, CaseId);

        Assert.True(route.ByMail);
        Assert.Equal(Customer, route.To);
        Assert.Equal("support@example.net", route.From);
        Assert.Equal(SupportCaseOptions.PrimaryMailboxName, route.Mailbox);
        Assert.Equal(MailPolicyEffect.None, route.Effect);
        Assert.Null(route.RedirectedTo);
    }

    [Fact]
    public async Task WhenThePolicyRedirects_TheRouteSaysWhereTo()
    {
        await MailedCaseAsync();
        Decide(OutboundMailAction.Redirect, Override);

        var route = await Staff().GetAnswerRouteAsync(TeamKey, CaseId);

        Assert.Equal((MailPolicyEffect.Redirect, Override), (route.Effect, route.RedirectedTo));
        Assert.Equal(Customer, route.To);
        _policy.Received().Decide(Customer, "Re: Export is empty");
    }

    [Fact]
    public async Task WhenThePolicyWithholds_TheRouteSaysItIsNotSent()
    {
        await MailedCaseAsync();
        Decide(OutboundMailAction.Withhold);

        var route = await Staff().GetAnswerRouteAsync(TeamKey, CaseId);

        Assert.Equal(MailPolicyEffect.Withhold, route.Effect);
        Assert.Null(route.RedirectedTo);
    }

    [Fact]
    public async Task WithNoPolicyRegistered_TheRouteNamesNoEffect()
    {
        await MailedCaseAsync();
        var channel = new EmailSupportChannel(_mail, _store, Options.Create(Primary()));

        var route = await Staff(channel).GetAnswerRouteAsync(TeamKey, CaseId);

        Assert.Equal(MailPolicyEffect.None, route.Effect);
    }

    [Fact]
    public async Task ACaseRaisedOnTheSite_IsAnsweredOnTheSite()
    {
        await _store.AddCaseAsync(Case() with { Bindings = [new SupportChannelBinding { ChannelType = SupportChannelType.Slack, ExternalId = "123.456" }] }, Opening(SupportChannelType.Slack));

        var route = await Staff().GetAnswerRouteAsync(TeamKey, CaseId);

        Assert.False(route.ByMail);
        Assert.Null(route.To);
        Assert.Null(await Staff().PreviewAnswerAsync(TeamKey, CaseId, null, "<p>Hi</p>"));
    }

    [Fact]
    public async Task WithoutMail_EveryCaseIsAnsweredOnTheSite()
    {
        await MailedCaseAsync();
        var authorizer = Authorizer("sam", systemScopes: [SystemSupportScopes.AllManage]);
        var service = new AuthorizationSupportCaseServiceDecorator(new SupportCaseService(_store, authorizer, TimeProvider.System), authorizer);

        Assert.False((await service.GetAnswerRouteAsync(TeamKey, CaseId)).ByMail);
    }

    [Fact]
    public async Task AnUnknownCase_HasNoRoute()
    {
        Assert.Null(await Staff().GetAnswerRouteAsync(TeamKey, "nothing"));
    }

    [Fact]
    public async Task AMailChannelOfTheHostsOwn_IsDescribedByTheAddressAlone_AndOffersNoPreview()
    {
        await MailedCaseAsync();
        var own = Substitute.For<ISupportChannel>();
        own.ChannelType.Returns(SupportChannelType.Email);
        var authorizer = Authorizer("sam", systemScopes: [SystemSupportScopes.AllManage]);
        var service = new AuthorizationSupportCaseServiceDecorator(new SupportCaseService(_store, authorizer, TimeProvider.System, [own]), authorizer);

        var route = await service.GetAnswerRouteAsync(TeamKey, CaseId);

        Assert.Equal((true, Customer, null, MailPolicyEffect.None), (route.ByMail, route.To, route.From, route.Effect));
        Assert.Null(await service.PreviewAnswerAsync(TeamKey, CaseId, null, "<p>Hi</p>"));
    }

    [Fact]
    public async Task ACaseOfAnAddedMailbox_IsAnsweredFromThatMailbox_AndOneNoLongerConfiguredFromThePrimary()
    {
        await _store.AddCaseAsync(Case() with { Bindings = [Binding() with { Mailbox = Hosted, MailboxAddress = "help@example.org" }] }, Opening());
        await _store.AddCaseAsync(Case() with { Id = "case-2", Bindings = [Binding() with { ExternalId = "opening-2@customer.example", Mailbox = "removed" }] }, Opening());
        var mailboxes = new SupportMailboxes(Primary(), [new SupportMailbox(Hosted, new MailOptions { FromAddress = "help@example.org" })]);
        var channel = new EmailSupportChannel(_mail, _store, Options.Create(Primary()), mailboxes: mailboxes, mailClients: new SupportMailClients(_mail, _policy), policy: _policy);

        var hosted = await Staff(channel).GetAnswerRouteAsync(TeamKey, CaseId);
        var removed = await Staff(channel).GetAnswerRouteAsync(TeamKey, "case-2");

        Assert.Equal((Hosted, "help@example.org"), (hosted.Mailbox, hosted.From));
        Assert.Equal((SupportCaseOptions.PrimaryMailboxName, "support@example.net"), (removed.Mailbox, removed.From));
    }

    [Fact]
    public async Task TheRouteAndThePreview_AreRefused_ToWhoeverMayNotAnswer()
    {
        await MailedCaseAsync();
        var member = Service(Authorizer("alice", TeamKey));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => member.GetAnswerRouteAsync(TeamKey, CaseId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => member.PreviewAnswerAsync(TeamKey, CaseId, null, "<p>Hi</p>"));
    }

    [Fact]
    public async Task ATeamsSupportManager_SeesTheRoute()
    {
        await MailedCaseAsync();

        var route = await Service(Authorizer("bob", TeamKey, teamScopes: [SupportScopes.Manage])).GetAnswerRouteAsync(TeamKey, CaseId);

        Assert.True(route.ByMail);
    }

    [Fact]
    public async Task ThePreview_IsTheAnswerMail_FromTheCasesMailbox_WithTheConversationQuoted()
    {
        await MailedCaseAsync();

        var preview = await Staff().PreviewAnswerAsync(TeamKey, CaseId, null, "<p>Try <b>this</b>.</p>");

        Assert.Equal(("Example Support", "support@example.net"), (MailboxAddress.Parse(preview.From).Name, MailboxAddress.Parse(preview.From).Address));
        Assert.Equal(Customer, preview.To);
        Assert.Equal("Re: Export is empty", preview.Subject);
        Assert.Null(preview.ReplyTo);
        Assert.Equal("<p>sam:</p><p>Try <b>this</b>.</p><p>2026-10-17 08:00 UTC, A Customer:</p><blockquote>The export is empty.</blockquote>", preview.Html);
        Assert.StartsWith("sam:\n\nTry this.\n\n2026-10-17 08:00 UTC, A Customer:\n> The export is empty.", preview.Text);
    }

    [Fact]
    public async Task ThePreview_SendsNothing_AndStoresNothing()
    {
        await MailedCaseAsync();

        await Staff().PreviewAnswerAsync(TeamKey, CaseId, null, "<p>Try <b>this</b>.</p>");

        await _mail.DidNotReceiveWithAnyArgs().SendAsync(default, default);
        Assert.Single((await _store.GetMessagesAsync(TeamKey, CaseId, null, 50)).Items);
        Assert.Single((await _store.GetCaseAsync(TeamKey, CaseId)).Bindings);
    }

    [Theory]
    [InlineData(false, "<p>Try <b>this</b>.</p>")]
    [InlineData(false, "<p>Plain words.</p>")]
    [InlineData(true, "<p>Try <b>this</b>.</p>")]
    [InlineData(true, "<p>Plain words.</p>")]
    public async Task ThePreview_IsTheMailThatIsSent(bool withTemplate, string answer)
    {
        await MailedCaseAsync();
        var template = withTemplate ? Template("<body><h1>{{subject}}</h1>{{content}}<i>{{footer}}</i></body>") : null;
        var staff = Staff(Channel(template));

        var preview = await staff.PreviewAnswerAsync(TeamKey, CaseId, null, answer);
        await staff.AnswerCaseAsync(TeamKey, CaseId, string.Empty, answer, []);

        await _mail.Received(1).SendAsync(Arg.Is<OutboundMail>(x => x.Subject == preview.Subject && x.To == preview.To && x.Body == preview.Text && x.BodyHtml == preview.Html), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFormattedAnswer_IsMailedInTheHostsTemplate()
    {
        await MailedCaseAsync();
        var staff = Staff(Channel(Template("<body><h1>{{subject}}</h1>{{content}}<i>{{footer}}</i></body>")));

        await staff.AnswerCaseAsync(TeamKey, CaseId, string.Empty, "<p>Try <b>this</b>.</p>", []);

        await _mail.Received(1).SendAsync(Arg.Is<OutboundMail>(x =>
                x.BodyHtml == "<body><h1>Re: Export is empty</h1><p>sam:</p><p>Try <b>this</b>.</p><p>2026-10-17 08:00 UTC, A Customer:</p><blockquote>The export is empty.</blockquote><i></i></body>"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APlainAnswer_GetsAnHtmlPartInTheTemplate_AndKeepsItsText()
    {
        await MailedCaseAsync();
        var staff = Staff(Channel(Template("<div>{{content}}</div>")));

        await staff.AnswerCaseAsync(TeamKey, CaseId, "Plain <words>.\nTwo lines.");

        await _mail.Received(1).SendAsync(Arg.Is<OutboundMail>(x =>
                x.BodyHtml == "<div><p>sam:</p><p>Plain &lt;words&gt;.<br>Two lines.</p><p>2026-10-17 08:00 UTC, A Customer:</p><blockquote>The export is empty.</blockquote></div>" &&
                x.Body.StartsWith("sam:\n\nPlain <words>.\nTwo lines.\n\n")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutATemplate_APlainAnswerIsStillTextAlone()
    {
        await MailedCaseAsync();

        await Staff().AnswerCaseAsync(TeamKey, CaseId, "Plain words.");

        await _mail.Received(1).SendAsync(Arg.Is<OutboundMail>(x => x.BodyHtml == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheSupportRegistration_FramesAnswersInTheRegisteredTemplate()
    {
        var services = new ServiceCollection();
        services.AddThargaMailTemplate(o => o.Html = "<div>{{content}}</div>");
        services.AddThargaSupportCases(o =>
        {
            o.Email.FromAddress = "support@example.net";
            o.Email.Smtp.Host = "smtp.example.net";
        });
        await _store.AddCaseAsync(Case() with { Bindings = [Binding()] }, Opening());
        services.AddSingleton<ISupportCaseStore>(_store);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mail = scope.ServiceProvider.GetServices<ISupportChannel>().OfType<ISupportAnswerMail>().Single();
        var supportCase = await _store.GetCaseAsync(TeamKey, CaseId);

        var preview = await mail.PreviewAsync(supportCase, Binding(), Opening() with { Sequence = 2, Source = null, Body = "Hi", AuthorName = "sam", AsSupport = true }, TestContext.Current.CancellationToken);

        Assert.StartsWith("<div><p>sam:</p><p>Hi</p>", preview.Html);
    }

    private void Decide(OutboundMailAction action, string redirectTo = null)
        => _policy.Decide(Arg.Any<string>(), Arg.Any<string>()).Returns(x => new OutboundMailDecision(action, redirectTo ?? x.ArgAt<string>(0), x.ArgAt<string>(1)));

    private async Task MailedCaseAsync() => await _store.AddCaseAsync(Case() with { Bindings = [Binding()] }, Opening());

    private EmailSupportChannel Channel(MailTemplate template = null)
        => new(_mail, _store, Options.Create(Primary()), policy: _policy, template: template);

    private ISupportCaseService Staff(EmailSupportChannel channel = null)
        => Service(Authorizer("sam", systemScopes: [SystemSupportScopes.AllManage]), channel ?? Channel());

    private ISupportCaseService Service(TeamAuthorizer authorizer, EmailSupportChannel channel = null)
        => new AuthorizationSupportCaseServiceDecorator(new SupportCaseService(_store, authorizer, TimeProvider.System, [channel ?? Channel()]), authorizer);

    private static MailTemplate Template(string html) => new(Options.Create(new MailTemplateOptions { Html = html }));

    private static MailOptions Primary() => new() { FromAddress = "support@example.net", FromName = "Example Support" };

    private static SupportChannelBinding Binding() => new() { ChannelType = SupportChannelType.Email, ExternalId = OpeningId, Address = Customer };

    private static SupportCase Case() => new()
    {
        Id = CaseId,
        TeamKey = TeamKey,
        AuthorIdentity = null,
        AuthorName = "A Customer",
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = Opened,
        MessageCount = 1
    };

    private static SupportMessage Opening(SupportChannelType source = SupportChannelType.Email) => new()
    {
        Sequence = 1,
        Kind = SupportMessageKind.User,
        AuthorName = "A Customer",
        Body = "The export is empty.",
        SentAt = Opened,
        Source = source,
        Delivery = SupportMessageDelivery.Sent
    };

    private static TeamAuthorizer Authorizer(string subject, string memberOfTeam = null, string[] teamScopes = null, string[] systemScopes = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, subject), new(ClaimTypes.Name, subject) };

        if (memberOfTeam != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, memberOfTeam));
        claims.AddRange((teamScopes ?? []).Select(x => new Claim(TeamClaimTypes.Scope, x)));
        claims.AddRange((systemScopes ?? []).Select(x => new Claim(TeamClaimTypes.SystemScope, x)));

        return new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
