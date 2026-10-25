using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The assistant answers with links, followed only when they stay on the site: a steered answer cannot send a member to
/// a page that only looks like this one.
/// </summary>
public class SupportAnswerLinksTests : BunitContext
{
    [Theory]
    [InlineData("See [your case](/support/abc).", "See [your case](/support/abc).")]
    [InlineData("See [the docs](https://evil.example/login).", "See the docs (https://evil.example/login).")]
    [InlineData("See [x](//evil.example).", "See x (//evil.example).")]
    [InlineData("See [x](/\\evil.example).", "See x (/\\evil.example).")]
    [InlineData("See [x](javascript:alert(1)).", "See x (javascript:alert(1)).")]
    [InlineData("No links here.", "No links here.")]
    public void OnlyLinksOnTheSite_StayLinks(string markdown, string expected)
    {
        Assert.Equal(expected, SupportAnswerLinks.SiteOnly(markdown));
    }

    [Fact]
    public void AnAnswerOnTheSupportPage_ShowsItsLink_ToACaseOnTheSite()
    {
        var view = RenderTranscript("Your case is [Export is empty](/support/case-7).");

        var link = view.Find("[data-body-markdown='2'] a");
        Assert.Equal("/support/case-7", link.GetAttribute("href"));
        Assert.Equal("Export is empty", link.TextContent);
    }

    [Fact]
    public void AnAnswerOnTheSupportPage_ShowsALinkElsewhere_AsText()
    {
        var view = RenderTranscript("Sign in at [our portal](https://evil.example/login), or https://evil.example/again.");

        var body = view.Find("[data-body-markdown='2']");
        Assert.Empty(body.QuerySelectorAll("a"));
        Assert.Contains("our portal (https://evil.example/login)", body.TextContent);
    }

    private IRenderedComponent<SupportTranscript> RenderTranscript(string answer)
    {
        Services.AddSingleton(Mock.Of<ISupportCaseService>());
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        SupportMessage[] messages =
        [
            new() { Sequence = 1, Kind = SupportMessageKind.User, AuthorIdentity = "alice", AuthorName = "Alice", Body = "Where is my case?", SentAt = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc) },
            new() { Sequence = 2, Kind = SupportMessageKind.Assistant, AuthorName = "Assistant", Body = answer, SentAt = new DateTime(2026, 10, 24, 12, 0, 1, DateTimeKind.Utc) }
        ];

        return Render<SupportTranscript>(p => p
            .Add(x => x.Messages, messages)
            .Add(x => x.CaseAuthorIdentity, "alice")
            .Add(x => x.AuthorOnRight, true)
            .Add(x => x.TeamKey, "acme")
            .Add(x => x.CaseId, "case-1"));
    }
}
