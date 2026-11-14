using System.Security.Claims;
using Microsoft.Extensions.Options;
using MimeKit;
using Tharga.Team.Email;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Support staff writing first: the case that is opened for the mail, the mail that goes out, and who may do it.
/// </summary>
/// <remarks>
/// Addresses are made up and on more than one domain; none is built into the package.
/// </remarks>
public class StartMailCaseTests
{
    private const string Main = "support@example.net";
    private const string Alias = "help@example.net";
    private const string Prospect = "lead@acme.example";
    private const string OtherProspect = "buyer@sample.test";
    private const string TeamKey = "team-a";

    private readonly InMemorySupportCaseStore _store = new();
    private readonly IMailClient _mail = Substitute.For<IMailClient>();
    private readonly List<SupportCaseUpdatedEventArgs> _notified = [];
    private OutboundMail _sent;

    public StartMailCaseTests()
    {
        _mail.CanSend.Returns(true);
        _mail.SendAsync(Arg.Do<OutboundMail>(x => _sent = x), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("first-1@example.net"));
    }

    [Fact]
    public async Task StartingACase_OpensItWithTheRecipientAsContact_AndTheMessageAsItsFirstEntry()
    {
        var started = await Staff(Manage).StartMailCaseAsync(Request());

        Assert.Null(started.TeamKey);
        Assert.Equal(Prospect, started.ExternalContact.Email);
        Assert.Equal("A Prospect", started.ExternalContact.Name);
        Assert.Equal("Our offer", started.Subject);
        Assert.Equal(SupportCaseStatus.Open, started.Status);

        var message = Assert.Single((await _store.GetMessagesAsync(null, started.Id, null, 50)).Items);
        Assert.Equal("Hello, here is our offer.", message.Body);
        Assert.True(message.AsSupport);
        Assert.Equal(SupportMessageDelivery.Sent, message.Delivery);
        Assert.Equal("anna", message.AuthorIdentity);
    }

    [Fact]
    public async Task TheMail_GoesToTheRecipient_WithTheSubjectAsWritten_AndTheTrackerInReplyTo()
    {
        var started = await Staff(Manage).StartMailCaseAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(Prospect, _sent.To);
        Assert.Equal("Our offer", _sent.Subject);
        Assert.StartsWith("Hello, here is our offer.", _sent.Body);
        Assert.Matches(@"^support\+[^@]+@example\.net$", _sent.ReplyTo);

        // The answer to it lands on the same case.
        var found = await CaseReplyAddress.FindByRecipientAsync(_store, [_sent.ReplyTo], Mailbox(), TestContext.Current.CancellationToken);
        Assert.Equal(started.Id, found?.Id);
    }

    [Fact]
    public async Task AReplyThatNamesTheMailsOwnId_FindsTheCase()
    {
        var started = await Staff(Manage).StartMailCaseAsync(Request());

        var found = await _store.GetCaseByBindingAsync(SupportChannelType.Email, "first-1@example.net");

        Assert.Equal(started.Id, found?.Id);
    }

    [Fact]
    public async Task ACaseStartedInATeam_BelongsToIt_AndTheBoardIsToldOfBoth()
    {
        var started = await Staff(AllManage).StartMailCaseAsync(Request() with { TeamKey = TeamKey, To = OtherProspect, ToName = null });

        Assert.Equal(TeamKey, started.TeamKey);
        Assert.Equal(OtherProspect, started.ExternalContact.Email);
        Assert.Equal(OtherProspect, started.AuthorName);
        Assert.Equal(OtherProspect, _sent.To);
        Assert.All(_notified, x => Assert.Equal(TeamKey, x.TeamKey));
        Assert.Contains(_notified, x => x.Change == SupportCaseChange.Raised && x.CaseId == started.Id);
    }

    [Fact]
    public async Task TheChosenAddress_IsUsed_AndRecordedOnTheEntry()
    {
        var started = await Staff(Manage, sendAs: [Alias]).StartMailCaseAsync(Request() with { Choice = new SupportAnswerChoice { SendFrom = Alias, SignWithName = true } });

        Assert.Equal(Alias, MailboxAddress.Parse(_sent.From).Address);
        var message = (await _store.GetMessagesAsync(null, started.Id, null, 50)).Items.Single();
        Assert.Equal(Alias, message.SentFrom);
        Assert.True(message.SignedWithName);
    }

    [Fact]
    public async Task AnAddressNoMailboxAllows_IsRefused_BeforeAnythingIsWritten()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Staff(Manage).StartMailCaseAsync(Request() with { Choice = new SupportAnswerChoice { SendFrom = "stranger@example.org" } }));

        await _mail.DidNotReceiveWithAnyArgs().SendAsync(default, default);
        Assert.Empty((await _store.GetUnassignedCasesAsync(null, 20)).Items);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not an address")]
    [InlineData("a@b.example, c@d.example")]
    public async Task ABadAddress_IsRefused_AndNothingIsSent(string address)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Staff(Manage).StartMailCaseAsync(Request() with { To = address }));

        await _mail.DidNotReceiveWithAnyArgs().SendAsync(default, default);
        Assert.Empty((await _store.GetUnassignedCasesAsync(null, 20)).Items);
    }

    [Fact]
    public async Task AnEmptyMessage_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Staff(Manage).StartMailCaseAsync(Request() with { Body = "  ", BodyHtml = null }));

        Assert.Empty((await _store.GetUnassignedCasesAsync(null, 20)).Items);
    }

    [Fact]
    public async Task WithoutASubject_ItIsDerivedFromTheMessage()
    {
        var started = await Staff(Manage).StartMailCaseAsync(Request() with { Subject = null, Body = "Thanks for filling in our form." });

        Assert.False(string.IsNullOrWhiteSpace(started.Subject));
        Assert.Equal(started.Subject, _sent.Subject);
    }

    [Fact]
    public async Task AMailServerThatFails_LeavesTheCase_WithTheMessageNotDelivered()
    {
        _mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Failed("connection refused"));

        var started = await Staff(Manage).StartMailCaseAsync(Request());

        var message = (await _store.GetMessagesAsync(null, started.Id, null, 50)).Items.Single();
        Assert.Equal(SupportMessageDelivery.Failed, message.Delivery);
        Assert.NotNull(await _store.GetCaseAsync(null, started.Id));
    }

    [Fact]
    public async Task WithNoMailSetUp_ItIsRefused()
    {
        var authorizer = Authorizer(Manage);
        var service = new AuthorizationSupportCaseServiceDecorator(new SupportCaseService(_store, authorizer, TimeProvider.System, []), authorizer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartMailCaseAsync(Request()));
    }

    [Fact]
    public async Task Attachments_AreStoredWithTheEntry()
    {
        var files = new InMemorySupportAttachmentStore();
        using var content = new MemoryStream([1, 2, 3]);

        var started = await Staff(Manage, files).StartMailCaseAsync(Request() with
        {
            Attachments = [new SupportAttachmentUpload { FileName = "offer.pdf", ContentType = "application/pdf", Content = content }]
        });

        var message = (await _store.GetMessagesAsync(null, started.Id, null, 50)).Items.Single();
        Assert.Equal("offer.pdf", Assert.Single(message.Attachments).FileName);
    }

    [Fact]
    public async Task ACaseWithNoTeam_NeedsTheUnassignedGrant_AndAMemberHasNone()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Staff().StartMailCaseAsync(Request()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Staff(SystemSupportScopes.Read).StartMailCaseAsync(Request()));

        await _mail.DidNotReceiveWithAnyArgs().SendAsync(default, default);
    }

    [Fact]
    public async Task ACaseInATeam_NeedsTheCrossTeamGrant_NotJustTheUnassignedOne()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Staff(Manage).StartMailCaseAsync(Request() with { TeamKey = TeamKey }));

        await _mail.DidNotReceiveWithAnyArgs().SendAsync(default, default);
    }

    [Fact]
    public async Task ACaseById_IsFound_WhicheverTeamItBelongsTo_WithTheGrantThatCoversIt()
    {
        var unassigned = await Staff(Manage).StartMailCaseAsync(Request());
        var team = await Staff(AllManage).StartMailCaseAsync(Request() with { TeamKey = TeamKey, To = OtherProspect });

        Assert.Equal(unassigned.Id, (await Staff(SystemSupportScopes.Read).GetCaseByIdAsync(unassigned.Id))?.Id);
        Assert.Equal(team.Id, (await Staff(SystemSupportScopes.AllRead).GetCaseByIdAsync(team.Id))?.Id);
        Assert.Equal(team.TeamKey, (await Staff(SystemSupportScopes.AllRead).GetCaseByIdAsync(team.Id)).TeamKey);
    }

    [Fact]
    public async Task ACaseById_NobodyHoldsTheGrantFor_IsRefused_AndAnUnknownIdIsNone()
    {
        var unassigned = await Staff(Manage).StartMailCaseAsync(Request());
        var team = await Staff(AllManage).StartMailCaseAsync(Request() with { TeamKey = TeamKey, To = OtherProspect });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Staff().GetCaseByIdAsync(unassigned.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Staff(SystemSupportScopes.Read).GetCaseByIdAsync(team.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Staff(SystemSupportScopes.AllRead).GetCaseByIdAsync(unassigned.Id));

        Assert.Null(await Staff(SystemSupportScopes.Read).GetCaseByIdAsync("no-such-case"));
        Assert.Null(await Staff(SystemSupportScopes.Read).GetCaseByIdAsync(null));
    }

    private static string Manage => SystemSupportScopes.Manage;

    private static string AllManage => SystemSupportScopes.AllManage;

    private static StartMailCaseRequest Request() => new()
    {
        To = Prospect,
        ToName = "A Prospect",
        Subject = "Our offer",
        Body = "Hello, here is our offer."
    };

    private static MailOptions Mailbox(string[] sendAs = null)
        => new()
        {
            FromAddress = Main,
            FromName = "Example Support",
            PerCaseReplyTo = true,
            SendAs = [.. (sendAs ?? []).Select(x => new MailSendAs { Address = x })]
        };

    private ISupportCaseService Staff(string scope = null, InMemorySupportAttachmentStore files = null, string[] sendAs = null)
        => Build(scope == null ? [] : [scope], files, sendAs);

    private ISupportCaseService Staff(string[] scopes) => Build(scopes, null, null);

    private ISupportCaseService Build(string[] scopes, InMemorySupportAttachmentStore files, string[] sendAs)
    {
        var options = Mailbox(sendAs);
        var authorizer = Authorizer(scopes);
        var channel = new EmailSupportChannel(_mail, _store, Options.Create(options));
        var notifier = Substitute.For<ISupportCaseNotifier>();
        notifier.When(x => x.Notify(Arg.Any<SupportCaseUpdatedEventArgs>())).Do(x => _notified.Add(x.Arg<SupportCaseUpdatedEventArgs>()));

        return new AuthorizationSupportCaseServiceDecorator(
            new SupportCaseService(_store, authorizer, TimeProvider.System, [channel], notifier, attachmentStore: files, mailboxes: new SupportMailboxes(options)),
            authorizer);
    }

    private static TeamAuthorizer Authorizer(params string[] scopes)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "anna"),
            new(ClaimTypes.Name, "Anna Andersson"),
            new(ClaimTypes.Email, "anna@example.net")
        };

        claims.AddRange(scopes.Select(x => new Claim(TeamClaimTypes.SystemScope, x)));

        return new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
