using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MimeKit;
using Tharga.Team.Email.Mailbox;
using Tharga.Team.Email.Tests.Users;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Mailbox;

/// <summary>
/// Reading a mail the postmaster holds as a person reads it — its text, its formatting made safe, the images it
/// shows and its files — for a view that cannot parse a mail itself.
/// </summary>
public class PostmasterBodyTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryTeamMailStore _mail = new();
    private readonly InMemoryContentStore _content = new();

    private PostmasterService Postmaster() => new(_mail, new TeamMailNotifier(), _content, null, null, new SteppingTimeProvider(Now));

    private async Task KeepAsync(string id, MimeMessage message, string teamKey = null)
    {
        await _mail.AddAsync(new InboundMailItem
        {
            Id = id,
            TeamKey = teamKey,
            Envelope = new MailEnvelope { MessageId = $"<{id}@example.org>", From = "anna@example.org", DeliveredTo = "oldkey-mail@example.net", Subject = message.Subject },
            Reason = MailRouteReason.UnknownAddress,
            ReceivedAt = Now
        });

        using var raw = new MemoryStream();
        message.WriteTo(raw);
        await _content.SaveAsync(id, raw.ToArray());
    }

    private static MimeMessage Message(MimeEntity body)
    {
        var message = new MimeMessage { Subject = "Where does this go?", Body = body, MessageId = "m1@example.org" };
        message.From.Add(new MailboxAddress("Anna", "anna@example.org"));
        message.To.Add(new MailboxAddress(null, "oldkey-mail@example.net"));
        return message;
    }

    private static MimePart File(string mediaType, string mediaSubtype, byte[] content, string fileName, string disposition = ContentDisposition.Attachment, string contentId = null)
    {
        var part = new MimePart(mediaType, mediaSubtype)
        {
            Content = new MimeContent(new MemoryStream(content)),
            ContentTransferEncoding = ContentEncoding.Base64,
            ContentDisposition = new ContentDisposition(disposition),
            FileName = fileName
        };

        if (contentId != null) part.ContentId = contentId;

        return part;
    }

    private static MimeMessage Formatted() => Message(new Multipart("mixed")
    {
        new MultipartAlternative
        {
            new TextPart("plain") { Text = "Hello,\n\nthe invoice is attached." },
            new MultipartRelated
            {
                new TextPart("html") { Text = "<p>Hello,</p><p><b>the invoice</b> is attached. <img src=\"cid:logo@example.org\" alt=\"Logo\"><img src=\"https://tracker.example.com/p.gif\" alt=\"pixel\"></p><script>alert(1)</script>" },
                File("image", "png", [1, 2, 3], "logo.png", ContentDisposition.Inline, "logo@example.org")
            }
        },
        File("application", "pdf", [9, 9, 9, 9], "invoice.pdf")
    });

    [Fact]
    public async Task APlainMail_ReadsAsItsText_WithNoFormattingAndNoFiles()
    {
        await KeepAsync("mail-1", Message(new TextPart("plain") { Text = "Is this the right address?\n\nAnna" }));

        var body = await Postmaster().GetBodyAsync("mail-1");

        Assert.Equal("mail-1", body.MailId);
        Assert.Equal("Is this the right address?\n\nAnna", body.Text.Replace("\r\n", "\n"));
        Assert.Null(body.Html);
        Assert.Empty(body.Attachments);
        Assert.Empty(body.Images);
    }

    [Fact]
    public async Task AFormattedMail_KeepsItsFormatting_Sanitized_WithNoRemoteImageAndNoScript()
    {
        await KeepAsync("mail-1", Formatted());

        var body = await Postmaster().GetBodyAsync("mail-1");

        Assert.Contains("<b>the invoice</b>", body.Html);
        Assert.Contains("src=\"cid:logo@example.org\"", body.Html);
        Assert.DoesNotContain("tracker.example.com", body.Html);
        Assert.Contains("pixel", body.Html);
        Assert.DoesNotContain("script", body.Html);
        Assert.Contains("the invoice is attached.", body.Text);
    }

    [Fact]
    public async Task AFormattedMailWithNoText_ReadsItsFormattingAsText()
    {
        await KeepAsync("mail-1", Message(new TextPart("html") { Text = "<p>Only <i>formatted</i> here.</p>" }));

        var body = await Postmaster().GetBodyAsync("mail-1");

        Assert.Equal("Only formatted here.", body.Text);
        Assert.Contains("<i>formatted</i>", body.Html);
    }

    [Fact]
    public async Task EveryFile_IsListedByPosition_WithItsNameTypeAndSize()
    {
        await KeepAsync("mail-1", Formatted());

        var body = await Postmaster().GetBodyAsync("mail-1");

        Assert.Equal(["1", "2"], body.Attachments.Select(x => x.Id));
        Assert.Equal(["logo.png", "invoice.pdf"], body.Attachments.Select(x => x.FileName));
        Assert.Equal(["image/png", "application/pdf"], body.Attachments.Select(x => x.ContentType));
        Assert.Equal([3L, 4L], body.Attachments.Select(x => x.Size));
        Assert.True(body.Attachments[0].Inline);
        Assert.Equal("logo@example.org", body.Attachments[0].ContentId);
    }

    [Fact]
    public async Task AnImageTheFormattingShows_IsCarriedToShowInPlace()
    {
        await KeepAsync("mail-1", Formatted());

        var image = Assert.Single((await Postmaster().GetBodyAsync("mail-1")).Images);

        Assert.Equal("logo@example.org", image.ContentId);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal([1, 2, 3], image.Content);
    }

    [Fact]
    public async Task AnImageTooLargeOrOfATypeThatCouldRunScript_IsOnlyListed()
    {
        var html = new TextPart("html") { Text = "<p><img src=\"cid:big@example.org\"><img src=\"cid:vector@example.org\"></p>" };
        var big = File("image", "png", new byte[ReceivedMailBody.MaxInlineImageBytes + 1], "big.png", ContentDisposition.Inline, "big@example.org");
        var vector = File("image", "svg+xml", [1], "vector.svg", ContentDisposition.Inline, "vector@example.org");
        await KeepAsync("mail-1", Message(new MultipartRelated { html, big, vector }));

        var body = await Postmaster().GetBodyAsync("mail-1");

        Assert.Empty(body.Images);
        Assert.Equal(["big.png", "vector.svg"], body.Attachments.Select(x => x.FileName));
    }

    [Fact]
    public async Task AFileIsOpenedByItsPosition_WithItsContent()
    {
        await KeepAsync("mail-1", Formatted());

        await using var file = await Postmaster().OpenAttachmentAsync("mail-1", "2");

        Assert.Equal("invoice.pdf", file.Attachment.FileName);
        Assert.Equal("2", file.Attachment.Id);
        using var read = new MemoryStream();
        await file.Content.CopyToAsync(read);
        Assert.Equal([9, 9, 9, 9], read.ToArray());
    }

    [Theory]
    [InlineData("3")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("first")]
    [InlineData("")]
    [InlineData(null)]
    public async Task AnIdThatNamesNoFile_OpensNothing(string attachmentId)
    {
        await KeepAsync("mail-1", Formatted());

        Assert.Null(await Postmaster().OpenAttachmentAsync("mail-1", attachmentId));
    }

    [Fact]
    public async Task ATeamsMail_IsNotReadThroughThePostmaster()
    {
        await KeepAsync("mail-1", Formatted(), teamKey: "team-a");

        Assert.Null(await Postmaster().GetBodyAsync("mail-1"));
        Assert.Null(await Postmaster().OpenAttachmentAsync("mail-1", "1"));
    }

    [Fact]
    public async Task AMailWhoseContentIsNoLongerKept_ReadsAsNothing()
    {
        await KeepAsync("mail-1", Formatted());
        await _content.DeleteAsync("mail-1");

        Assert.Null(await Postmaster().GetBodyAsync("mail-1"));
        Assert.Null(await Postmaster().OpenAttachmentAsync("mail-1", "1"));
    }

    [Fact]
    public async Task AnArchivedMail_CanStillBeRead()
    {
        await KeepAsync("mail-1", Formatted());
        await Postmaster().ArchiveAsync("mail-1");

        Assert.NotNull(await Postmaster().GetBodyAsync("mail-1"));
        Assert.NotNull(await Postmaster().OpenAttachmentAsync("mail-1", "1"));
    }

    [Fact]
    public void ContentThatIsNoMail_ReadsAsEmpty_RatherThanFailingThePage()
    {
        var body = ReceivedMailReader.Body("mail-1", []);

        Assert.Equal(string.Empty, body.Text);
        Assert.Null(body.Html);
        Assert.Empty(body.Attachments);
        Assert.Null(ReceivedMailReader.Open([], "1"));
    }

    [Fact]
    public async Task ThroughTheProxy_ReadingTakesTheReadScope_AndEachReadIsRecorded()
    {
        await KeepAsync("mail-1", Formatted());
        var audit = new RecordingAuditLogger();
        var postmaster = Resolve(Staff(new Claim(TeamClaimTypes.SystemScope, SystemMailboxScopes.PostmasterRead)), audit);

        Assert.NotNull(await postmaster.GetBodyAsync("mail-1"));
        await using (var file = await postmaster.OpenAttachmentAsync("mail-1", "1")) Assert.NotNull(file);

        Assert.Equal([nameof(IPostmasterService.GetBodyAsync), nameof(IPostmasterService.OpenAttachmentAsync)], audit.Entries.Select(x => x.MethodName));
        Assert.All(audit.Entries, x => Assert.Equal(AuditEventType.ServiceCall, x.EventType));
    }

    [Fact]
    public async Task ThroughTheProxy_WithoutTheReadScope_NothingIsRead()
    {
        await KeepAsync("mail-1", Formatted());
        var postmaster = Resolve(Staff(new Claim(TeamClaimTypes.SystemScope, SystemMailboxScopes.PostmasterManage)), new RecordingAuditLogger());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => postmaster.GetBodyAsync("mail-1"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => postmaster.OpenAttachmentAsync("mail-1", "1"));
    }

    private static ClaimsPrincipal Staff(params Claim[] extra)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Sam Staff"), new Claim(ClaimTypes.NameIdentifier, "sam"), .. extra], "Test"));

    private IPostmasterService Resolve(ClaimsPrincipal caller, RecordingAuditLogger audit)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITeamPrincipalAccessor>(new FixedPrincipal(caller));
        services.AddSingleton<ITeamMailKeyStore>(new InMemoryTeamMailKeyStore());
        services.AddSingleton<ITeamMailStore>(_mail);
        services.AddSingleton<IInboundMailContentStore>(_content);
        services.AddSingleton(new CompositeAuditLogger([audit], Options.Create(new AuditOptions())));
        services.AddThargaTeamMailbox(o => o.Domain = "example.net");

        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IPostmasterService>();
    }

    private sealed class FixedPrincipal(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
