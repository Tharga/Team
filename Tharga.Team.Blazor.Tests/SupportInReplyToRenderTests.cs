using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The mail a support case's first mail answered, shown above the transcript to staff who may read the mail log (Tharga/Team#529).
/// </summary>
/// <remarks>
/// <b>Rendered, and the button clicked</b>, as the sibling views are tested. A reader who may not read the log is told the case is a reply and
/// is shown nothing of the mail, and a mail that expired is said to be no longer kept rather than leaving the case silent.
/// </remarks>
public class SupportInReplyToRenderTests : BunitContext
{
    private const string MailId = "mail-1";

    private readonly Mock<IUserMailHistoryService> _history = new();

    public SupportInReplyToRenderTests()
    {
        Services.AddSingleton(_history.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static UserMailContent Sent(string body = "Welcome to Acme.", string html = null) => new()
    {
        Mail = new UserMailHistoryItem
        {
            Id = MailId,
            Kind = UserMailKind.Mail,
            Category = "account",
            Subject = "Your invitation to Acme",
            Status = UserMailStatus.Sent,
            Address = "user@customer.example",
            QueuedAt = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc),
            ChangedAt = new DateTime(2026, 11, 1, 8, 1, 0, DateTimeKind.Utc)
        },
        Body = body,
        BodyHtml = html
    };

    private IRenderedComponent<SupportInReplyTo> Render(string mailId = MailId) => Render<SupportInReplyTo>(p => p.Add(x => x.MailId, mailId));

    [Fact]
    public void TheMailAnswered_ShowsItsSubject_Recipient_AndWhenItWasSent()
    {
        _history.Setup(x => x.GetMailContentAsync(MailId, default)).ReturnsAsync(Sent());

        var view = Render();

        Assert.Equal("Your invitation to Acme", view.Find("[data-in-reply-subject]").TextContent.Trim());
        var meta = view.Find("[data-in-reply-meta]").TextContent;
        Assert.Contains("user@customer.example", meta);
        Assert.Contains("sent", meta);
    }

    [Fact]
    public void TheBody_IsHiddenUntilAsked_AndThenShown()
    {
        _history.Setup(x => x.GetMailContentAsync(MailId, default)).ReturnsAsync(Sent());

        var view = Render();

        Assert.Empty(view.FindAll("[data-in-reply-body]"));

        view.Find("[data-in-reply-toggle]").Click();

        Assert.Contains("Welcome to Acme.", view.Find("[data-in-reply-body]").TextContent);

        view.Find("[data-in-reply-toggle]").Click();

        Assert.Empty(view.FindAll("[data-in-reply-body]"));
    }

    [Fact]
    public void AMailThatExpired_IsSaidToBeNoLongerKept()
    {
        _history.Setup(x => x.GetMailContentAsync(MailId, default)).ReturnsAsync((UserMailContent)null);

        var view = Render();

        Assert.Equal("gone", view.Find("[data-in-reply-to]").GetAttribute("data-state"));
        Assert.Contains("no longer kept", view.Markup);
    }

    /// <summary>The case is a reply, which the reader may know; what the mail said is not theirs to read.</summary>
    [Fact]
    public void AReaderWhoMayNotReadTheLog_IsToldItIsAReply_ButSeesNothingOfTheMail()
    {
        _history.Setup(x => x.GetMailContentAsync(MailId, default)).ThrowsAsync(new UnauthorizedAccessException());

        var view = Render();

        Assert.Equal("denied", view.Find("[data-in-reply-to]").GetAttribute("data-state"));
        Assert.DoesNotContain("Your invitation", view.Markup);
        Assert.DoesNotContain("Welcome to Acme.", view.Markup);
        Assert.Empty(view.FindAll("[data-in-reply-toggle]"));
    }

    [Fact]
    public void WithNoMailId_NothingIsShown_AndTheLogIsNotAsked()
    {
        var view = Render(mailId: null);

        Assert.Empty(view.FindAll("[data-in-reply-to]"));
        _history.Verify(x => x.GetMailContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>A host whose own history service does not implement the read gets no block rather than an error.</summary>
    [Fact]
    public void AHistoryServiceThatCannotReadMailContent_ShowsNothing()
    {
        _history.Setup(x => x.GetMailContentAsync(MailId, default)).ThrowsAsync(new NotSupportedException());

        var view = Render();

        Assert.Empty(view.FindAll("[data-in-reply-to]"));
    }

    [Fact]
    public void TheTranscript_ShowsTheBlockAboveTheFirstMessage_OnlyWhereAnIdIsPassed()
    {
        _history.Setup(x => x.GetMailContentAsync(MailId, default)).ReturnsAsync(Sent());
        Services.AddSingleton(Mock.Of<ISupportCaseService>());
        Services.AddSingleton(Mock.Of<IIconResolver>());

        var withId = Render<SupportTranscript>(p => p.Add(x => x.InReplyToMailId, MailId).Add(x => x.Messages, [FirstMessage()]));
        var without = Render<SupportTranscript>(p => p.Add(x => x.Messages, [FirstMessage()]));

        Assert.Single(withId.FindAll("[data-in-reply-to]"));
        Assert.Empty(without.FindAll("[data-in-reply-to]"));
        Assert.True(withId.Markup.IndexOf("data-in-reply-to", StringComparison.Ordinal) < withId.Markup.IndexOf("I cannot sign in.", StringComparison.Ordinal));
    }

    private static SupportMessage FirstMessage() => new()
    {
        Sequence = 1,
        Kind = SupportMessageKind.User,
        AuthorName = "user@customer.example",
        Body = "I cannot sign in.",
        SentAt = new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc)
    };
}
