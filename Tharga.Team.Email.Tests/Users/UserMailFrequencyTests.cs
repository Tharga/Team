using Tharga.Team.Email.Users;
using Tharga.Team.Service;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>
/// How often each category is sent: mail sent or bounced to users over the past year, or the shorter period the outbox
/// keeps, averaged over the users who received any, per month, from one count kept for a few minutes; and the words for it.
/// </summary>
public class UserMailFrequencyTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    [Theory]
    [InlineData(0, UserMailTexts.FrequencyNoneKey, 0)]
    [InlineData(0.05, UserMailTexts.FrequencyUnderOneAYearKey, 0)]
    [InlineData(0.0834, UserMailTexts.FrequencyPerYearKey, 1)]
    [InlineData(0.2, UserMailTexts.FrequencyPerYearKey, 2)]
    [InlineData(0.5, UserMailTexts.FrequencyPerYearKey, 6)]
    [InlineData(0.95, UserMailTexts.FrequencyPerYearKey, 11)]
    [InlineData(0.96, UserMailTexts.FrequencyPerMonthKey, 1)]
    [InlineData(1, UserMailTexts.FrequencyPerMonthKey, 1)]
    [InlineData(2.4, UserMailTexts.FrequencyPerMonthKey, 2)]
    [InlineData(2.5, UserMailTexts.FrequencyPerMonthKey, 3)]
    [InlineData(4.49, UserMailTexts.FrequencyPerMonthKey, 4)]
    [InlineData(4.5, UserMailTexts.FrequencyPerWeekKey, 1)]
    [InlineData(8.7, UserMailTexts.FrequencyPerWeekKey, 2)]
    [InlineData(30.4, UserMailTexts.FrequencyPerWeekKey, 7)]
    public void TheWords_FollowHowOftenAMonth(double perMonth, string key, int count)
    {
        var phrase = UserMailFrequencyPhrase.Of(perMonth);

        Assert.Equal((key, count), (phrase.Key, phrase.Count));
    }

    [Theory]
    [InlineData(0, "None in the past year")]
    [InlineData(0.05, "Fewer than one a year")]
    [InlineData(0.25, "About 3 a year")]
    [InlineData(2, "About 2 a month")]
    [InlineData(13, "About 3 a week")]
    public void TheWords_ReadInEnglishByDefault(double perMonth, string expected)
    {
        var phrase = UserMailFrequencyPhrase.Of(perMonth);

        Assert.Equal(expected, phrase.Format(phrase.English));
    }

    [Fact]
    public void ATranslation_TakesTheNumber_AndOneThatDoesNotFormat_FallsBackToEnglish()
    {
        var phrase = UserMailFrequencyPhrase.Of(2);

        Assert.Equal("Cirka 2 i månaden", phrase.Format("Cirka {0} i månaden"));
        Assert.Equal("About 2 a month", phrase.Format("Cirka {1} i månaden"));
        Assert.Equal("About 2 a month", phrase.Format(null));
    }

    [Fact]
    public void NoneSent_OverAShorterPeriodThanAYear_IsNoneRecently()
    {
        var now = UserMailFixture.Start;
        var frequency = new UserMailCategoryFrequency { Category = "news", Since = now - TimeSpan.FromDays(90), Until = now };

        Assert.False(frequency.CoversAYear);
        Assert.Equal(UserMailTexts.FrequencyNoneRecentlyKey, frequency.Phrase.Key);
        Assert.Equal(UserMailTexts.FrequencyNoneKey, (frequency with { Since = now - TimeSpan.FromDays(365) }).Phrase.Key);
    }

    [Fact]
    public void TheFigure_IsTheMailsPerUserWhoReceivedAny_PerMonth()
    {
        var now = UserMailFixture.Start;
        var frequency = new UserMailCategoryFrequency { Category = "news", Sent = 48, Recipients = 2, Since = now - TimeSpan.FromDays(365), Until = now };

        Assert.Equal(24.0 / 12 * 365.25 / 365, frequency.PerMonth, 6);
        Assert.Equal((UserMailTexts.FrequencyPerMonthKey, 2), (frequency.Phrase.Key, frequency.Phrase.Count));
    }

    [Fact]
    public async Task EveryCategory_IsCounted_SentAndBouncedAlike_AverageOverThoseWhoReceivedAny()
    {
        var fixture = new UserMailFixture();
        fixture.Outbox.Add(fixture.Done(UserMailFixture.News, "anna", UserMailStatus.Sent, 10 * Day));
        fixture.Outbox.Add(fixture.Done(UserMailFixture.News, "anna", UserMailStatus.Bounced, 40 * Day));
        fixture.Outbox.Add(fixture.Done(UserMailFixture.News, "bo", UserMailStatus.Sent, 70 * Day));
        fixture.Outbox.Add(fixture.Done(UserMailFixture.News, "cia", UserMailStatus.Skipped, 5 * Day));
        fixture.Outbox.Add(fixture.Done(UserMailFixture.News, "dan", UserMailStatus.Failed, 5 * Day));

        var frequencies = await fixture.Frequencies.GetAsync(None);

        Assert.Equal(["account", "service", "news", "digest"], frequencies.Select(x => x.Category));
        var news = frequencies.Single(x => x.Category == UserMailFixture.News);
        Assert.Equal((3, 2), (news.Sent, news.Recipients));
        Assert.Equal(UserMailFixture.Start, news.Until);
        Assert.Equal(UserMailFixture.Start - TimeSpan.FromDays(365), news.Since);
        Assert.Equal((0, 0), (frequencies.Single(x => x.Category == UserMailFixture.Digest).Sent, frequencies.Single(x => x.Category == UserMailFixture.Digest).Recipients));
    }

    [Fact]
    public async Task MailOlderThanAYear_AndMailToAnAddressAlone_AreNotCounted()
    {
        var fixture = new UserMailFixture();
        fixture.Outbox.Add(fixture.Done(UserMailCategories.AccountKey, "anna", UserMailStatus.Sent, 400 * Day));
        fixture.Outbox.Add(fixture.Done(UserMailCategories.AccountKey, null, UserMailStatus.Sent, 3 * Day));
        fixture.Outbox.Add(fixture.Done(UserMailCategories.AccountKey, "bo", UserMailStatus.Sent, 3 * Day));

        var account = (await fixture.Frequencies.GetAsync(None)).Single(x => x.Category == UserMailCategories.AccountKey);

        Assert.Equal((1, 1), (account.Sent, account.Recipients));
    }

    [Fact]
    public async Task AnOutboxKeepingDoneMailForLessThanAYear_IsCountedOverWhatItKeeps()
    {
        var fixture = new UserMailFixture();
        fixture.Outbox.Retention = TimeSpan.FromDays(90);
        fixture.Outbox.Add(fixture.Done(UserMailFixture.News, "anna", UserMailStatus.Sent, 10 * Day));

        var news = (await fixture.Frequencies.GetAsync(None)).Single(x => x.Category == UserMailFixture.News);

        Assert.Equal(UserMailFixture.Start - TimeSpan.FromDays(90), news.Since);
        Assert.False(news.CoversAYear);
        Assert.Equal((UserMailTexts.FrequencyPerYearKey, 4), (news.Phrase.Key, news.Phrase.Count));
    }

    [Fact]
    public async Task TheCount_IsKeptForAFewMinutes_ThenMadeAgain()
    {
        var fixture = new UserMailFixture();

        await fixture.Frequencies.GetAsync(None);
        fixture.Outbox.Add(fixture.Done(UserMailFixture.News, "anna", UserMailStatus.Sent, Day));
        var kept = await fixture.Frequencies.GetAsync(None);
        fixture.Time.Advance(UserMailFrequencies.CacheDuration);
        var again = await fixture.Frequencies.GetAsync(None);

        Assert.Equal(2, fixture.Outbox.Counts);
        Assert.Equal(0, kept.Single(x => x.Category == UserMailFixture.News).Sent);
        Assert.Equal(1, again.Single(x => x.Category == UserMailFixture.News).Sent);
    }

    [Fact]
    public async Task AnOutboxThatCannotCount_GivesNoFigures()
    {
        var fixture = new UserMailFixture();
        var outbox = Substitute.For<IUserMailOutbox>();
        outbox.CountSentByCategoryAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns<Task<IReadOnlyList<UserMailCategorySent>>>(_ => throw new NotSupportedException());

        var frequencies = await new UserMailFrequencies(outbox, fixture.Categories, new UserMailFrequencyCache(), fixture.Time).GetAsync(None);

        Assert.Empty(frequencies);
    }

    [Fact]
    public async Task ThePreferenceService_OffersTheFrequencies_ToAnyCaller()
    {
        var fixture = new UserMailFixture();
        fixture.Outbox.Add(fixture.Done(UserMailFixture.News, "anna", UserMailStatus.Sent, Day));
        var service = new UserMailPreferenceService(fixture.Preferences, fixture.Categories, new TeamAuthorizer(Substitute.For<ITeamPrincipalAccessor>()), Substitute.For<IUserService>(), fixture.Options, fixture.Frequencies);

        var frequencies = await service.GetCategoryFrequenciesAsync(None);

        Assert.Equal(1, frequencies.Single(x => x.Category == UserMailFixture.News).Sent);
    }
}
