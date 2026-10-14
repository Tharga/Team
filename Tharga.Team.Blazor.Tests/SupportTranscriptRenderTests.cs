using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// A case reads as a chat: who raised it on one side, everybody answering on the other, each with their own mark.
/// </summary>
public class SupportTranscriptRenderTests : BunitContext
{
    private const string Author = "alice";

    private static SupportMessage Message(int sequence, SupportMessageKind kind, string identity, string name, string body) => new()
    {
        Sequence = sequence,
        Kind = kind,
        AuthorIdentity = identity,
        AuthorName = name,
        Body = body,
        SentAt = new DateTime(2026, 9, 3, 8, sequence, 0, DateTimeKind.Utc)
    };

    private static readonly SupportMessage[] Conversation =
    [
        Message(1, SupportMessageKind.User, Author, "Alice", "The export is empty."),
        Message(2, SupportMessageKind.Assistant, null, "Assistant", "Try the export button."),
        Message(3, SupportMessageKind.User, "sam", "Sam Support", "Looking into it."),
        Message(4, SupportMessageKind.System, "sam", "Sam Support", "Case closed by Sam Support.")
    ];

    [Fact]
    public void TheReader_SeesTheirOwnMessagesOnTheRight_AndTheAnswersOnTheLeft()
    {
        var view = Render(authorOnRight: true);

        var sides = view.FindAll("[data-side]").Select(x => x.GetAttribute("data-side")).ToArray();

        Assert.Equal(["right", "left", "left"], sides);
    }

    [Fact]
    public void SupportStaff_SeeTheCustomerOnTheLeft_AndTheAnswersOnTheRight()
    {
        var view = Render(authorOnRight: false);

        var sides = view.FindAll("[data-side]").Select(x => x.GetAttribute("data-side")).ToArray();

        Assert.Equal(["left", "right", "right"], sides);
    }

    [Fact]
    public void EachSenderCarriesTheirOwnMark()
    {
        var view = Render(authorOnRight: true);

        var senders = view.FindAll("[data-sender]").Select(x => x.GetAttribute("data-sender")).ToArray();

        Assert.Equal(["author", "assistant", "support", "system"], senders);
    }

    [Fact]
    public void TheAssistantIsAnIcon_NotAWordedLabel()
    {
        var view = Render(authorOnRight: true);

        Assert.Contains("smart_toy", view.Markup);
        Assert.DoesNotContain(">Assistant<", view.Markup);
    }

    [Fact]
    public void ASupportAnswer_NamesThePersonWhoWroteIt()
    {
        var view = Render(authorOnRight: true);

        Assert.Contains("support_agent", view.Markup);
        Assert.Contains("Sam Support", view.Markup);
    }

    private IRenderedComponent<SupportTranscript> Render(bool authorOnRight)
    {
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<SupportTranscript>(parameters => parameters
            .Add(x => x.Messages, Conversation)
            .Add(x => x.CaseAuthorIdentity, Author)
            .Add(x => x.AuthorOnRight, authorOnRight));
    }
}
