using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// An answer mailed to the customer carries the files it was written with, read from the store; one that cannot
/// carry them all is not sent and says why.
/// </summary>
public class EmailSupportChannelAttachmentTests
{
    private const string OpeningId = "opening-1@example.org";
    private const string Correspondent = "user@example.com";
    private const string CaseId = "case-1";

    [Fact]
    public async Task AnAnswerWithFiles_IsMailedWithThem_InOrder()
    {
        var (channel, mail, files) = Build();
        var first = await files.SaveAsync(CaseId, Upload("report.pdf", [1, 2, 3], "application/pdf"));
        var second = await files.SaveAsync(CaseId, Upload("chart.png", [4], "image/png"));

        var result = await channel.PostWithResultAsync(Binding(), Answer(first, second));

        Assert.True(result.Delivered);
        await mail.Received(1).SendAsync(Arg.Is<OutboundMail>(x =>
                x.Attachments.Count == 2 &&
                x.Attachments[0].FileName == "report.pdf" && x.Attachments[0].ContentType == "application/pdf" && x.Attachments[0].Content.SequenceEqual(new byte[] { 1, 2, 3 }) &&
                x.Attachments[1].FileName == "chart.png"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAnswerWithoutFiles_IsMailedWithNone()
    {
        var (channel, mail, _) = Build();

        await channel.PostWithResultAsync(Binding(), Answer());

        await mail.Received(1).SendAsync(Arg.Is<OutboundMail>(x => x.Attachments.Count == 0), Arg.Any<CancellationToken>());
    }

    /// <summary>"See the attached" arriving without the attachment misleads; held back, it is visible and can be sent again.</summary>
    [Fact]
    public async Task AFileThatCannotBeRead_HoldsTheAnswerBack_AndSaysWhich()
    {
        var (channel, mail, _) = Build();
        var missing = new SupportAttachment { Id = "gone", FileName = "gone.pdf", ContentType = "application/pdf", Size = 1 };

        var result = await channel.PostWithResultAsync(Binding(), Answer(missing));

        Assert.False(result.Delivered);
        Assert.Contains("gone.pdf", result.Error);
        await mail.DidNotReceive().SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNoStore_AnAnswerWithFiles_IsHeldBack()
    {
        var (channel, mail, _) = Build(withStore: false);

        var result = await channel.PostWithResultAsync(Binding(), Answer(new SupportAttachment { Id = "a", FileName = "a.pdf", ContentType = "application/pdf", Size = 1 }));

        Assert.False(result.Delivered);
        Assert.Contains("attachment store", result.Error);
        await mail.DidNotReceive().SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFileOfAnotherCase_IsNotSent()
    {
        var (channel, mail, files) = Build();
        var foreign = await files.SaveAsync("case-2", Upload("theirs.pdf", [9], "application/pdf"));

        var result = await channel.PostWithResultAsync(Binding(), Answer(foreign));

        Assert.False(result.Delivered);
        await mail.DidNotReceive().SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>());
    }

    private static SupportAttachmentUpload Upload(string name, byte[] content, string contentType)
        => new() { FileName = name, ContentType = contentType, Content = new MemoryStream(content) };

    private static SupportMessage Answer(params SupportAttachment[] attachments) => new()
    {
        Sequence = 2,
        Kind = SupportMessageKind.User,
        AuthorIdentity = "sub-2",
        AuthorName = "Support Agent",
        Body = "Attached.",
        SentAt = new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc),
        AsSupport = true,
        Attachments = attachments
    };

    private static SupportChannelBinding Binding() => new()
    {
        ChannelType = SupportChannelType.Email,
        ExternalId = OpeningId,
        Address = Correspondent
    };

    private static (ISupportChannel Channel, IMailClient Mail, InMemorySupportAttachmentStore Files) Build(bool withStore = true)
    {
        var mail = Substitute.For<IMailClient>();
        mail.CanSend.Returns(true);
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("answer-1@example.org"));

        var store = Substitute.For<ISupportCaseStore>();
        store.GetCaseByBindingAsync(SupportChannelType.Email, OpeningId, Arg.Any<CancellationToken>()).Returns(new SupportCase
        {
            Id = CaseId,
            TeamKey = "acme",
            AuthorIdentity = "sub-1",
            AuthorName = "A User",
            Subject = "Export is empty",
            Status = SupportCaseStatus.Open,
            CreatedAt = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc),
            MessageCount = 1
        });
        store.GetMessagesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new SupportMessagePage { Items = [] });

        var files = new InMemorySupportAttachmentStore();
        var options = Options.Create(new MailOptions { FromAddress = "support@example.org" });

        return (new EmailSupportChannel(mail, store, options, withStore ? files : null), mail, files);
    }
}
