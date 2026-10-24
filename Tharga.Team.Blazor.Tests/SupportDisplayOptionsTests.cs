using Blazored.LocalStorage;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// What the host chooses to show beside the assistant's answers: whether they are rated, and who sees the service calls
/// behind them.
/// </summary>
public class SupportDisplayOptionsTests : BunitContext
{
    private static readonly DateTime At = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private static SupportMessage[] Conversation() =>
    [
        new() { Sequence = 1, Kind = SupportMessageKind.User, AuthorIdentity = "alice", AuthorName = "Alice", Body = "Where is my export case?", SentAt = At },
        new()
        {
            Sequence = 2, Kind = SupportMessageKind.Assistant, AuthorName = "Assistant", Body = "Here it is.", SentAt = At.AddSeconds(1),
            Rating = SupportAnswerRating.Up, AssistantCalls = [new() { Name = "search_my_cases", Arguments = "text: \"export\"" }]
        }
    ];

    [Fact]
    public void ByDefault_AnswersAreRated_AndEverybodySeesTheCalls()
    {
        var member = RenderTranscript(asMember: true);

        Assert.NotEmpty(member.FindAll("[data-rate-choice]"));
        Assert.NotNull(member.Find("[data-calls='2']"));
    }

    [Fact]
    public void WithRatingOff_TheMemberHasNoThumbs_AndSupportSeesNoMark()
    {
        var member = RenderTranscript(asMember: true, o => o.RateAnswers = false);
        var staff = Render<SupportTranscript>(p => p.Add(x => x.Messages, Conversation()).Add(x => x.CaseAuthorIdentity, "alice").Add(x => x.AuthorOnRight, false));

        Assert.Empty(member.FindAll("[data-rate]"));
        Assert.Empty(staff.FindAll("[data-rated]"));
    }

    [Fact]
    public void CallsShownToSupport_AreHiddenFromTheMember()
    {
        var member = RenderTranscript(asMember: true, o => o.AnswerDetailsShownTo = SupportAnswerDetailsAudience.Support);
        var staff = Render<SupportTranscript>(p => p.Add(x => x.Messages, Conversation()).Add(x => x.CaseAuthorIdentity, "alice").Add(x => x.AuthorOnRight, false));

        Assert.Empty(member.FindAll("[data-calls]"));
        Assert.NotNull(staff.Find("[data-calls='2']"));
    }

    [Fact]
    public void CallsShownToNobody_AreHiddenFromSupportToo()
    {
        RenderTranscript(asMember: true, o => o.AnswerDetailsShownTo = SupportAnswerDetailsAudience.Nobody);
        var staff = Render<SupportTranscript>(p => p.Add(x => x.Messages, Conversation()).Add(x => x.CaseAuthorIdentity, "alice").Add(x => x.AuthorOnRight, false));

        Assert.Empty(staff.FindAll("[data-calls]"));
    }

    /// <summary>Set on the <c>AddThargaTeam</c> facade's Blazor section, as a host would.</summary>
    [Fact]
    public void TheSettings_AreReadFromTheBlazorOptions()
    {
        var options = new ThargaBlazorOptions();
        options.Support.RateAnswers = false;
        Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));

        Assert.False(SupportDisplay.Of(Services).RateAnswers);
    }

    [Fact]
    public void WithoutTheBlazorOptions_TheDefaultsHold()
    {
        var display = SupportDisplay.Of(new ServiceCollection().BuildServiceProvider());

        Assert.True(display.RateAnswers);
        Assert.Equal(SupportAnswerDetailsAudience.Everyone, display.AnswerDetailsShownTo);
    }

    private IRenderedComponent<SupportTranscript> RenderTranscript(bool asMember, Action<SupportDisplayOptions> configure = null)
    {
        Services.Configure<ThargaBlazorOptions>(o => configure?.Invoke(o.Support));
        Services.AddSingleton(Mock.Of<ISupportCaseService>());
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<SupportTranscript>(p =>
        {
            p.Add(x => x.Messages, Conversation())
                .Add(x => x.CaseAuthorIdentity, "alice")
                .Add(x => x.AuthorOnRight, asMember);
            if (asMember) p.Add(x => x.Rate, _ => { });
        });
    }
}
