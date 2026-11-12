using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Support.Cases;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The reason a case was closed, read in the conversation at the closure.
/// </summary>
public class SupportCloseCommentRenderTests : BunitContext
{
    private static readonly DateTime Noon = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AClosureWithAComment_ShowsItAsTheReason_BesideTheClosure()
    {
        var view = RenderTranscript(Closure("Answered by phone."));

        var comment = view.Find("[data-close-comment='2']");

        Assert.Contains("Reason:", comment.TextContent);
        Assert.Contains("Answered by phone.", comment.TextContent);
        Assert.Contains("Case closed by Sam Support.", view.Find("[data-sender='system']").TextContent);
    }

    [Fact]
    public void AClosureWithoutAComment_ShowsNoReason()
    {
        var view = RenderTranscript(Closure(null));

        Assert.Empty(view.FindAll("[data-close-comment]"));
        Assert.Contains("Case closed by Sam Support.", view.Find("[data-sender='system']").TextContent);
    }

    [Fact]
    public void TheComment_IsShownAsText_NotAsMarkup()
    {
        var view = RenderTranscript(Closure("<b>bold</b> <script>alert(1)</script>"));

        var comment = view.Find("[data-close-comment='2']");

        Assert.Empty(comment.QuerySelectorAll("script"));
        Assert.Empty(comment.QuerySelectorAll("b"));
        Assert.Contains("<b>bold</b>", comment.TextContent);
    }

    private static SupportMessage Closure(string comment) => new()
    {
        Sequence = 2,
        Kind = SupportMessageKind.System,
        AuthorIdentity = "staff",
        AuthorName = "Sam Support",
        Body = "Case closed by Sam Support.",
        InternalComment = comment,
        SentAt = Noon.AddMinutes(5)
    };

    private IRenderedComponent<SupportTranscript> RenderTranscript(SupportMessage closure)
    {
        Services.AddSingleton(Mock.Of<ISupportCaseService>());
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        SupportMessage[] messages =
        [
            new() { Sequence = 1, Kind = SupportMessageKind.User, AuthorIdentity = "alice", AuthorName = "Alice", Body = "Where is my case?", SentAt = Noon },
            closure
        ];

        return Render<SupportTranscript>(p => p
            .Add(x => x.Messages, messages)
            .Add(x => x.CaseAuthorIdentity, "alice")
            .Add(x => x.AuthorOnRight, false)
            .Add(x => x.TeamKey, "acme")
            .Add(x => x.CaseId, "case-1"));
    }
}
