using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Thumbs up or down on the assistant's answers, for the person it answered; a mark for everybody else.
/// </summary>
public class SupportAnswerRatingViewTests : BunitContext
{
    private static readonly DateTime At = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private static SupportMessage[] Conversation(SupportAnswerRating rating = SupportAnswerRating.None) =>
    [
        new() { Sequence = 1, Kind = SupportMessageKind.User, AuthorIdentity = "alice", AuthorName = "Alice", Body = "The export is empty.", SentAt = At },
        new() { Sequence = 2, Kind = SupportMessageKind.Assistant, AuthorName = "Assistant", Body = "Check the date range.", SentAt = At.AddSeconds(1), Rating = rating }
    ];

    [Fact]
    public void ThePersonAnswered_CanRateTheAssistantsAnswers_AndOnlyThose()
    {
        var view = RenderTranscript(Conversation(), rate: _ => { });

        Assert.Single(view.FindAll("[data-rate]"));
        Assert.NotNull(view.Find("[data-rate='2'] [data-rate-choice='Up']"));
        Assert.NotNull(view.Find("[data-rate='2'] [data-rate-choice='Down']"));
    }

    [Fact]
    public void RatingAnAnswer_SaysWhichAndHow_AndTheSameAgainTakesItBack()
    {
        (SupportMessage Message, SupportAnswerRating Rating) seen = default;
        var view = RenderTranscript(Conversation(SupportAnswerRating.Up), rate: x => seen = x);

        Assert.Equal("true", view.Find("[data-rate-choice='Up']").GetAttribute("aria-pressed"));

        view.Find("[data-rate-choice='Down']").Click();
        Assert.Equal((2, SupportAnswerRating.Down), (seen.Message.Sequence, seen.Rating));

        view.Find("[data-rate-choice='Up']").Click();
        Assert.Equal(SupportAnswerRating.None, seen.Rating);
    }

    [Fact]
    public void ForSupport_ARatedAnswerIsMarked_AndCannotBeRated()
    {
        var view = RenderTranscript(Conversation(SupportAnswerRating.Down), rate: null);

        Assert.Empty(view.FindAll("[data-rate]"));
        Assert.Equal("Rated not helpful", view.Find("[data-rated='Down']").GetAttribute("title"));
    }

    [Fact]
    public void AnUnratedAnswer_CarriesNoMark()
    {
        var view = RenderTranscript(Conversation(), rate: null);

        Assert.Empty(view.FindAll("[data-rated]"));
    }

    private IRenderedComponent<SupportTranscript> RenderTranscript(SupportMessage[] messages, Action<(SupportMessage Message, SupportAnswerRating Rating)> rate)
    {
        Services.Configure<ThargaBlazorOptions>(o => o.Support.RateAnswers = true);
        Services.AddSingleton(Mock.Of<ISupportCaseService>());
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<SupportTranscript>(p =>
        {
            p.Add(x => x.Messages, messages)
                .Add(x => x.CaseAuthorIdentity, "alice")
                .Add(x => x.AuthorOnRight, rate != null)
                .Add(x => x.TeamKey, "acme")
                .Add(x => x.CaseId, "case-1");
            if (rate != null) p.Add(x => x.Rate, rate);
        });
    }
}
