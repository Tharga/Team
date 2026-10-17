using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The files of a support mail are kept under the case and named on the message; what cannot be kept is said
/// in the message, never dropped silently.
/// </summary>
/// <remarks>
/// <b>A received mail is never refused for its files.</b> The ledger has recorded it before any file is looked
/// at, so a file that is too large, one too many, or a store that is down leaves a note where the file would
/// have been and the rest of the mail still reaches the case.
/// </remarks>
public class EmailAttachmentTests
{
    private const string TeamKey = "acme";
    private const string CaseId = "case-1";
    private const string Correspondent = "user@example.com";
    private const string ThreadId = "opening-1@example.org";

    private static InboundAttachment File(string name, int size = 3, bool inline = false, string contentId = null)
        => new(name, "application/pdf", Enumerable.Repeat((byte)7, size).ToArray(), inline, contentId);

    [Fact]
    public async Task AReplyWithFiles_StoresEachUnderTheCase_AndTheMessageNamesThem()
    {
        var (handler, store, files) = Build();

        await handler.HandleAsync(Reply() with { Attachments = [File("one.pdf"), File("two.pdf", 5)] });

        Assert.Equal(["one.pdf", "two.pdf"], files.Files.Select(x => x.Attachment.FileName).ToArray());
        Assert.All(files.Files, x => Assert.Equal(CaseId, x.CaseId));
        await store.Received(1).AppendMessageAsync(TeamKey, CaseId,
            Arg.Is<SupportMessage>(x => x.Attachments.Length == 2 && x.Attachments[1].FileName == "two.pdf" && x.Attachments[1].Size == 5 && x.Body == "See attached."),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ANewMailWithFiles_StoresThemUnderTheNewCase_AndItsFirstMessageNamesThem()
    {
        var (handler, store, files) = Build();
        SupportCase opened = null;
        SupportMessage first = null;
        await store.AddCaseAsync(Arg.Do<SupportCase>(x => opened = x), Arg.Do<SupportMessage>(x => first = x), Arg.Any<CancellationToken>());

        var outcome = await handler.HandleAsync(NewThread() with { Attachments = [File("scan.pdf")] });

        Assert.True(outcome.OpenedCase);
        var stored = Assert.Single(files.Files);
        Assert.Equal(opened.Id, stored.CaseId);
        Assert.Equal(stored.Attachment, Assert.Single(first.Attachments));
    }

    [Fact]
    public async Task AFileOverTheLimit_IsNotStored_AndTheMessageSaysWhichAndWhy()
    {
        var (handler, store, files) = Build(new MailOptions { MaxAttachmentBytes = 4 });

        await handler.HandleAsync(Reply() with { Attachments = [File("huge.zip", 5), File("small.pdf", 2)] });

        Assert.Equal(["small.pdf"], files.Files.Select(x => x.Attachment.FileName).ToArray());
        await store.Received(1).AppendMessageAsync(TeamKey, CaseId,
            Arg.Is<SupportMessage>(x => x.Body.Contains("huge.zip was not stored") && x.Body.Contains("5 bytes") && x.Attachments.Length == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FilesPastTheCount_AreNotStored_AndAreNamed()
    {
        var (handler, store, files) = Build(new MailOptions { MaxAttachmentCount = 1 });

        await handler.HandleAsync(Reply() with { Attachments = [File("first.pdf"), File("second.pdf")] });

        Assert.Single(files.Files);
        await store.Received(1).AppendMessageAsync(TeamKey, CaseId,
            Arg.Is<SupportMessage>(x => x.Body.Contains("second.pdf was not stored") && x.Body.Contains("at most 1 files")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FilesPastTheTotal_AreNotStored_AndAreNamed()
    {
        var (handler, store, files) = Build(new MailOptions { MaxAttachmentTotalBytes = 5 });

        await handler.HandleAsync(Reply() with { Attachments = [File("first.pdf", 4), File("second.pdf", 4)] });

        Assert.Single(files.Files);
        await store.Received(1).AppendMessageAsync(TeamKey, CaseId,
            Arg.Is<SupportMessage>(x => x.Body.Contains("second.pdf was not stored")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStoreThatFails_LeavesANote_AndTheMailStillReachesTheCase()
    {
        var (handler, store, files) = Build(attachments: new InMemorySupportAttachmentStore { FailsFor = name => name == "broken.pdf" });

        var outcome = await handler.HandleAsync(Reply() with { Attachments = [File("broken.pdf"), File("fine.pdf")] });

        Assert.True(outcome.WasApplied);
        Assert.Equal(["fine.pdf"], files.Files.Select(x => x.Attachment.FileName).ToArray());
        await store.Received(1).AppendMessageAsync(TeamKey, CaseId,
            Arg.Is<SupportMessage>(x => x.Body.Contains("broken.pdf could not be stored") && x.Attachments.Length == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNoStore_EachFileIsNamedAsNotStored()
    {
        var (handler, store, _) = Build(withStore: false);

        await handler.HandleAsync(Reply() with { Attachments = [File("a.pdf"), File("b.pdf")] });

        await store.Received(1).AppendMessageAsync(TeamKey, CaseId,
            Arg.Is<SupportMessage>(x => x.Body.Contains("a.pdf was not stored") && x.Body.Contains("b.pdf was not stored") && x.Attachments.Length == 0),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A scan sent with no text is a message; ignoring it as empty would lose what the customer sent.</summary>
    [Fact]
    public async Task AMailWithOnlyAFile_IsKept()
    {
        var (handler, store, files) = Build();

        var outcome = await handler.HandleAsync(Reply() with { Body = "", Attachments = [File("scan.pdf")] });

        Assert.True(outcome.WasApplied);
        Assert.Single(files.Files);
        await store.Received(1).AppendMessageAsync(TeamKey, CaseId, Arg.Is<SupportMessage>(x => x.Body == string.Empty && x.Attachments.Length == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ANewMailWithOnlyAFile_IsNamedAfterTheFile_WhenItHasNoSubject()
    {
        var (handler, store, _) = Build();
        SupportCase opened = null;
        await store.AddCaseAsync(Arg.Do<SupportCase>(x => opened = x), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());

        await handler.HandleAsync(NewThread() with { Subject = "", Body = "", Attachments = [File("invoice.pdf")] });

        Assert.Equal("invoice.pdf", opened.Subject);
    }

    [Fact]
    public async Task AnInlineImage_KeepsItsContentId_ForAnHtmlBodyToName()
    {
        var (handler, _, files) = Build();

        await handler.HandleAsync(Reply() with { Attachments = [File("logo.png", inline: true, contentId: "logo@example.com")] });

        var stored = Assert.Single(files.Files).Attachment;
        Assert.True(stored.Inline);
        Assert.Equal("logo@example.com", stored.ContentId);
    }

    [Fact]
    public async Task TheStoredContent_IsTheFileExactly()
    {
        var (handler, _, files) = Build();

        await handler.HandleAsync(Reply() with { Attachments = [new InboundAttachment("data.bin", "application/octet-stream", [1, 2, 3, 4])] });

        Assert.Equal([1, 2, 3, 4], Assert.Single(files.Files).Content);
    }

    private static InboundMail Reply() => new(
        MessageId: "reply-1@example.com",
        From: Correspondent,
        DeliveredTo: ["support@example.org"],
        Subject: "Re: Export is empty",
        Body: "See attached.",
        SentAt: new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero),
        InReplyTo: ThreadId,
        References: [ThreadId],
        HadAttachments: true);

    private static InboundMail NewThread() => Reply() with
    {
        MessageId = "first-1@example.com",
        From = "stranger@example.com",
        Subject = "Export is empty",
        Body = "Nothing comes out.",
        InReplyTo = null,
        References = []
    };

    private static SupportCase Case() => new()
    {
        Id = CaseId,
        TeamKey = TeamKey,
        AuthorIdentity = "sub-1",
        AuthorName = "A User",
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc),
        MessageCount = 1,
        Bindings = [new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = ThreadId, Address = Correspondent }]
    };

    private static (EmailEventHandler Handler, ISupportCaseStore Store, InMemorySupportAttachmentStore Files) Build(
        MailOptions options = null,
        InMemorySupportAttachmentStore attachments = null,
        bool withStore = true)
    {
        var store = Substitute.For<ISupportCaseStore>();
        store.GetCaseByBindingAsync(SupportChannelType.Email, ThreadId, Arg.Any<CancellationToken>()).Returns(Case());

        var ledger = Substitute.For<ISupportEventLedger>();
        ledger.TryRecordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var mailOptions = options ?? new MailOptions();
        mailOptions.FromAddress = "support@example.org";

        var files = attachments ?? new InMemorySupportAttachmentStore();

        var handler = new EmailEventHandler(store, ledger, Options.Create(mailOptions), TimeProvider.System, attachmentStore: withStore ? files : null);

        return (handler, store, files);
    }
}
