using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// A formatted reply or answer is sanitized by the service whatever the caller did, and its text is derived from it,
/// so what search, notifications and Slack read always says what the formatting says.
/// </summary>
public class SupportRichTextServiceTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Bob = "bob-subject";

    [Fact]
    public async Task AFormattedAnswer_IsKeptSanitized_WithItsTextDerived()
    {
        var cases = new InMemorySupportCaseStore();
        var raised = await Member(cases, Alice).RaiseCaseAsync(TeamA, "Export", "It is empty.");

        await Member(cases, Bob, SupportScopes.Manage).AnswerCaseAsync(TeamA, raised.Id, null,
            "<p onclick=\"steal()\">Try <b>this</b>:</p><ul><li>Restart</li></ul><script>steal()</script><p><a href=\"https://example.com/help\">Help</a></p>", []);

        var answer = (await Member(cases, Alice).GetMessagesAsync(TeamA, raised.Id)).Items[^1];
        Assert.True(answer.AsSupport);
        Assert.Equal("<p>Try <b>this</b>:</p><ul><li>Restart</li></ul><p><a href=\"https://example.com/help\" rel=\"noopener noreferrer\" target=\"_blank\">Help</a></p>", answer.BodyHtml);
        Assert.Equal("Try this:\n\n- Restart\n\nHelp (https://example.com/help)", answer.Body);
    }

    [Fact]
    public async Task AFormattedReply_CarriesItsFiles()
    {
        var cases = new InMemorySupportCaseStore();
        var files = new InMemorySupportAttachmentStore();
        var alice = Member(cases, Alice, files: files);
        var raised = await alice.RaiseCaseAsync(TeamA, "Export", "It is empty.");

        await alice.ReplyToCaseAsync(TeamA, raised.Id, null, "<p><i>Screenshot</i> attached.</p>",
            [new SupportAttachmentUpload { FileName = "shot.png", ContentType = "image/png", Content = new MemoryStream([1, 2]) }]);

        var reply = (await alice.GetMessagesAsync(TeamA, raised.Id)).Items[^1];
        Assert.Equal("<p><i>Screenshot</i> attached.</p>", reply.BodyHtml);
        Assert.Equal("Screenshot attached.", reply.Body);
        Assert.Equal("shot.png", Assert.Single(reply.Attachments).FileName);
    }

    [Theory]
    [InlineData("Hello<div>World</div>", "Hello\nWorld")]
    [InlineData("<p>One</p><p>Two</p>", "One\n\nTwo")]
    [InlineData("<p><span style=\"color:red\">Red</span></p>", "Red")]
    public void HtmlWithoutFormatting_IsKeptAsTextAlone(string html, string text)
    {
        Assert.Equal((text, null), SupportCaseService.Content("ignored", html));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<p><br></p>")]
    [InlineData("<script>x()</script>")]
    public void HtmlWithNothingReadable_LeavesTheTextAsWritten(string html)
    {
        Assert.Equal(("Plain words", null), SupportCaseService.Content("Plain words", html));
    }

    [Fact]
    public void FormattingTooLongToKeep_IsDropped_AndTheTextKept()
    {
        var html = string.Concat(Enumerable.Repeat("<p><b>x</b></p>", SupportCaseLimits.MaxMessageHtmlLength / 14 + 1));

        var (body, bodyHtml) = SupportCaseService.Content(null, html);

        Assert.Null(bodyHtml);
        Assert.StartsWith("x\n\nx", body);
    }

    /// <summary>A file written on the site is an attachment, so an image in the HTML could only point at nothing, or somewhere else.</summary>
    [Fact]
    public void Images_AreReplacedByTheirAltText()
    {
        var (_, bodyHtml) = SupportCaseService.Content(null, "<p><b>See</b> <img src=\"cid:x@y\" alt=\"chart\"> and <img src=\"https://tracker.example/p.gif\"></p>");

        Assert.Equal("<p><b>See</b> chart and </p>", bodyHtml);
    }

    [Fact]
    public async Task TextTooLongOnceDerived_IsRefusedAsPlainTextIs()
    {
        var cases = new InMemorySupportCaseStore();
        var alice = Member(cases, Alice);
        var raised = await alice.RaiseCaseAsync(TeamA, "Export", "It is empty.");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            alice.ReplyToCaseAsync(TeamA, raised.Id, null, $"<p><b>{new string('x', SupportCaseLimits.MaxMessageLength + 1)}</b></p>", []));
    }

    [Fact]
    public async Task AFormattedAnswer_IsAuthorizedAsAPlainAnswerIs()
    {
        var cases = new InMemorySupportCaseStore();
        var alice = Member(cases, Alice);
        var raised = await alice.RaiseCaseAsync(TeamA, "Export", "It is empty.");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => alice.AnswerCaseAsync(TeamA, raised.Id, null, "<p><b>Solved</b></p>", []));
    }

    [Fact]
    public async Task AFormattedReply_IsAuthorizedAsAPlainReplyIs()
    {
        var cases = new InMemorySupportCaseStore();
        var raised = await Member(cases, Alice).RaiseCaseAsync(TeamA, "Export", "It is empty.");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Member(cases, "stranger", team: "team-b").ReplyToCaseAsync(TeamA, raised.Id, null, "<p><b>Hi</b></p>", []));
    }

    private static ISupportCaseService Member(InMemorySupportCaseStore cases, string subject, string teamScope = null, InMemorySupportAttachmentStore files = null, string team = TeamA)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, subject),
            new(TeamClaimTypes.TeamKey, team)
        };

        if (teamScope != null) claims.Add(new Claim(TeamClaimTypes.Scope, teamScope));

        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));

        return new AuthorizationSupportCaseServiceDecorator(
            new SupportCaseService(cases, authorizer, TimeProvider.System, attachmentStore: files ?? new InMemorySupportAttachmentStore()),
            authorizer);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
