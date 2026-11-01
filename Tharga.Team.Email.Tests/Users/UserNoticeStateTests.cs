using Tharga.Team.Email.Users;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>
/// A notice about state rather than events: at most one mail per user per interval, written by the host from what is true
/// when it goes out, and what that mail covered remembered per subject.
/// </summary>
/// <remarks>
/// <b>An occurrence means "this user may have something to hear about", not "this is what to tell them".</b> So occurrences
/// that arrive while the interval holds are not replayed later; they wait and are composed together, against the state at
/// the time.
/// </remarks>
public class UserNoticeStateTests
{
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);
    private static readonly string Stream = UserNoticeMail.AtOnceStream("channel.expiring");

    private static UserNoticeType Channels(TimeSpan? interval = null, Func<UserNoticeComposition, IServiceProvider, CancellationToken, Task<UserNoticeComposed>> composer = null, UserNoticeDelivery[] deliveries = null) => new()
    {
        Key = "channel.expiring",
        Name = "Channels that need attention",
        Category = UserNoticeFixture.Work,
        Audience = UserNoticeAudience.NamedOnly,
        Deliveries = deliveries ?? [UserNoticeDelivery.AtOnce],
        Subject = "Channels need attention",
        MinimumInterval = interval,
        Composer = composer
    };

    private static UserNoticeFixture Fixture(UserNoticeType notice) => new(o => o.Add(notice));

    private static Task Raise(UserNoticeFixture fixture, UserNoticeType notice, string channel, string team = UserNoticeFixture.Alpha, string discriminator = null)
        => fixture.RaiseAsync(notice, channel, team, recipients: ["anna"], discriminator: discriminator);

    private static async Task Pass(UserNoticeFixture fixture, TimeSpan by)
    {
        fixture.Mail.Time.Advance(by);
        await fixture.RunAsync();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ANoticeThatAllowsTheDailySummary_CannotHaveAnIntervalOrAComposer(bool interval, bool composer)
    {
        var notice = Channels(interval ? Week : null, composer ? (_, _, _) => Task.FromResult<UserNoticeComposed>(null) : null, [UserNoticeDelivery.AtOnce, UserNoticeDelivery.Digest]);

        var error = Assert.Throws<InvalidOperationException>(() => new UserNoticeFixture(o => o.Add(notice)));

        Assert.Contains("channel.expiring", error.Message);
        Assert.Contains("at once", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void AnIntervalThatIsNotPositive_IsRefused(int minutes)
    {
        var notice = Channels(TimeSpan.FromMinutes(minutes));

        var error = Assert.Throws<InvalidOperationException>(() => new UserNoticeFixture(o => o.Add(notice)));

        Assert.Contains("MinimumInterval", error.Message);
    }

    [Fact]
    public void AnIntervalOrAComposer_WithAtOnceOnly_IsAccepted()
    {
        var withBoth = Channels(Week, (_, _, _) => Task.FromResult<UserNoticeComposed>(null));

        Assert.NotNull(new UserNoticeFixture(o => o.Add(withBoth)).Notices.Find("channel.expiring"));
        Assert.True(withBoth.IsStateful);
        Assert.False(Channels().IsStateful);
    }

    [Fact]
    public async Task OccurrencesWhileTheIntervalHolds_WaitAndGoTogetherWhenItIsOver()
    {
        var notice = Channels(Week);
        var fixture = Fixture(notice);

        await Raise(fixture, notice, "channel-1");
        await Pass(fixture, TimeSpan.FromMinutes(3));
        Assert.Single(fixture.Queued);

        await Raise(fixture, notice, "channel-2");
        await Pass(fixture, TimeSpan.FromMinutes(3));
        await Raise(fixture, notice, "channel-3");
        await Pass(fixture, TimeSpan.FromDays(6));
        Assert.Single(fixture.Queued);
        Assert.All(fixture.Outbox.Items.Where(x => x.SubjectId != "channel-1"), x => Assert.Equal(UserNoticeItemStatus.Waiting, x.Status));

        await Pass(fixture, TimeSpan.FromDays(1.1));

        Assert.Equal(2, fixture.Queued.Count);
        Assert.Equal(["channel-1", "channel-2", "channel-3"], fixture.Outbox.Items.Select(x => x.SubjectId).Order(StringComparer.Ordinal));
        Assert.All(fixture.Outbox.Items, x => Assert.Equal(UserNoticeItemStatus.Mailed, x.Status));

        var held = fixture.Outbox.Items.Where(x => x.SubjectId != "channel-1").Select(x => x.MailId).Distinct().ToArray();
        Assert.Single(held);
        Assert.NotEqual(fixture.Outbox.Items.Single(x => x.SubjectId == "channel-1").MailId, held[0]);
    }

    [Fact]
    public async Task TheInterval_IsPerUser_SoAPersonInTwoTeamsGetsOneMailCoveringBoth()
    {
        var notice = Channels(Week);
        var fixture = Fixture(notice);

        await Raise(fixture, notice, "channel-a", UserNoticeFixture.Alpha);
        await Raise(fixture, notice, "channel-b", UserNoticeFixture.Beta);
        await Pass(fixture, TimeSpan.FromMinutes(3));

        var mail = Assert.Single(fixture.Queued);
        Assert.Equal("anna", mail.Mail.RecipientUserKey);
        Assert.Null(mail.Mail.TeamKey);
        Assert.Equal(2, fixture.Outbox.Items.Count);
        Assert.Single(fixture.Outbox.Items.Select(x => x.MailId).Distinct());
    }

    [Fact]
    public async Task WithAnIntervalOnly_TheHeldOccurrencesAreMailedAsTheNoticeNormallyReads()
    {
        var notice = Channels(Week);
        var fixture = Fixture(notice);

        await Raise(fixture, notice, "channel-1");
        await Pass(fixture, TimeSpan.FromMinutes(3));
        await Raise(fixture, notice, "channel-2");
        await Raise(fixture, notice, "channel-3");
        await Pass(fixture, Week);

        var second = fixture.Queued[1].Mail;
        Assert.Contains("Channels that need attention", second.Subject);
        Assert.Equal(2, second.BodyHtml.Split("<li>").Length - 1);
    }

    /// <summary>Two instances, or an occurrence distributed before the last mail was recorded: the stream is checked again when it is claimed.</summary>
    [Fact]
    public async Task AStreamThatBecameDueTooEarly_IsHeldAtFlush_NotMailed()
    {
        var notice = Channels(Week);
        var fixture = Fixture(notice);

        await Raise(fixture, notice, "channel-1");
        await fixture.Distributor().DistributeNextAsync("instance-a", CancellationToken.None);
        fixture.Mail.Time.Advance(TimeSpan.FromMinutes(3));
        await fixture.Outbox.RecordCoverageAsync("anna", Stream, fixture.Mail.Time.GetUtcNow().UtcDateTime, ["channel-0"]);

        await fixture.Distributor().FlushNextAsync("instance-a", CancellationToken.None);

        Assert.Empty(fixture.Queued);
        Assert.Equal(UserNoticeItemStatus.Waiting, Assert.Single(fixture.Outbox.Items).Status);
        var stream = fixture.Outbox.StreamOf("anna", Stream);
        Assert.Equal(fixture.Mail.Time.GetUtcNow().UtcDateTime + Week, stream.Value.DueAt);
        Assert.Null(stream.Value.ClaimedBy);
    }

    [Fact]
    public async Task TheComposer_WritesTheMail_FromWhatItIsGiven()
    {
        UserNoticeComposition seen = null;
        var notice = Channels(Week, (composition, services, _) =>
        {
            seen = composition;
            Assert.NotNull(services.GetService(typeof(UserNoticeFixture)));

            return Task.FromResult(new UserNoticeComposed { Subject = "2 channels need attention", BodyHtml = "<p>Channel <b>one</b> expires.</p>", CoveredSubjects = ["channel-1", "channel-2"] });
        });
        var fixture = Fixture(notice);

        await Raise(fixture, notice, "channel-1", UserNoticeFixture.Alpha);
        await Raise(fixture, notice, "channel-2", UserNoticeFixture.Beta);
        await Pass(fixture, TimeSpan.FromMinutes(3));

        var mail = Assert.Single(fixture.Queued).Mail;
        Assert.Equal("2 channels need attention", mail.Subject);
        Assert.Equal("<p>Channel <b>one</b> expires.</p>", mail.BodyHtml);
        Assert.Equal(UserNoticeFixture.Work, mail.Category);
        Assert.Equal("anna", mail.RecipientUserKey);

        Assert.Equal("channel.expiring", seen.NoticeKey);
        Assert.Equal("anna", seen.UserKey);
        Assert.Equal([UserNoticeFixture.Alpha, UserNoticeFixture.Beta], seen.TeamKeys.Order(StringComparer.Ordinal));
        Assert.Equal(["channel-1", "channel-2"], seen.Waiting.Select(x => x.Occurrence.SubjectId).Order(StringComparer.Ordinal));
        Assert.All(seen.Waiting, x => Assert.Equal("channel.expiring", x.Occurrence.NoticeKey));
        Assert.Null(seen.LastMail.LastMailedAt);
        Assert.Empty(seen.LastMail.Subjects);
    }

    [Fact]
    public async Task TheSubjectsAMailCovered_AreRecordedOnItsItems_AndAsCoveredNow()
    {
        var notice = Channels(Week, (_, _, _) => Task.FromResult(new UserNoticeComposed { Subject = "Channels", BodyHtml = "<p>x</p>", CoveredSubjects = ["channel-1", "channel-9"] }));
        var fixture = Fixture(notice);

        await Raise(fixture, notice, "channel-1");
        await Pass(fixture, TimeSpan.FromMinutes(3));

        var item = Assert.Single(fixture.Outbox.Items);
        Assert.Equal(UserNoticeItemStatus.Mailed, item.Status);
        Assert.Equal(["channel-1", "channel-9"], item.CoveredSubjects);

        var coverage = await fixture.Outbox.GetCoverageAsync("anna", Stream);
        Assert.Equal(fixture.Mail.Time.GetUtcNow().UtcDateTime, coverage.LastMailedAt);
        Assert.Equal(["channel-1", "channel-9"], coverage.Subjects.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheNextComposition_IsToldWhatTheLastMailCovered_AndWhen_PerSubject()
    {
        var compositions = new List<UserNoticeComposition>();
        var covered = new Queue<string[]>([["channel-1", "channel-2"], ["channel-2"], []]);
        var notice = Channels(Week, (composition, _, _) =>
        {
            compositions.Add(composition);
            return Task.FromResult(new UserNoticeComposed { Subject = "Channels", BodyHtml = "<p>x</p>", CoveredSubjects = covered.Dequeue() });
        });
        var fixture = Fixture(notice);

        await Raise(fixture, notice, "channel-1");
        await Pass(fixture, TimeSpan.FromMinutes(3));
        var first = fixture.Mail.Time.GetUtcNow().UtcDateTime;

        await Raise(fixture, notice, "channel-2", discriminator: "again");
        await Pass(fixture, Week);
        var second = fixture.Mail.Time.GetUtcNow().UtcDateTime;
        await Raise(fixture, notice, "channel-3");
        await Pass(fixture, Week);

        Assert.Equal(3, compositions.Count);
        Assert.Equal(first, compositions[1].LastMail.LastMailedAt);
        Assert.Equal(first, compositions[1].LastMail.Subjects["channel-1"]);
        Assert.Equal(first, compositions[1].LastMail.Subjects["channel-2"]);

        // channel-1 was not in the second mail, so it keeps the time it was last mentioned: "still broken four weeks after".
        Assert.Equal(second, compositions[2].LastMail.LastMailedAt);
        Assert.Equal(first, compositions[2].LastMail.Subjects["channel-1"]);
        Assert.Equal(second, compositions[2].LastMail.Subjects["channel-2"]);
    }

    [Fact]
    public async Task AComposerThatReturnsNothing_SendsNothing_AndDropsWhatWaited()
    {
        var notice = Channels(Week, (_, _, _) => Task.FromResult<UserNoticeComposed>(null));
        var fixture = Fixture(notice);

        await Raise(fixture, notice, "channel-1");
        await Pass(fixture, TimeSpan.FromMinutes(3));

        Assert.Empty(fixture.Queued);
        var item = Assert.Single(fixture.Outbox.Items);
        Assert.Equal(UserNoticeItemStatus.Skipped, item.Status);
        Assert.Contains("nothing to tell", item.Reason);
    }

    [Fact]
    public async Task NothingSent_DoesNotStartTheInterval()
    {
        var results = new Queue<UserNoticeComposed>([null, new UserNoticeComposed { Subject = "Channels", BodyHtml = "<p>x</p>", CoveredSubjects = ["channel-2"] }]);
        var compositions = new List<UserNoticeComposition>();
        var notice = Channels(Week, (composition, _, _) =>
        {
            compositions.Add(composition);
            return Task.FromResult(results.Dequeue());
        });
        var fixture = Fixture(notice);

        await Raise(fixture, notice, "channel-1");
        await Pass(fixture, TimeSpan.FromMinutes(3));
        Assert.Empty(fixture.Queued);
        Assert.Null((await fixture.Outbox.GetCoverageAsync("anna", Stream)).LastMailedAt);

        await Raise(fixture, notice, "channel-2");
        await Pass(fixture, TimeSpan.FromMinutes(3));

        Assert.Single(fixture.Queued);
        Assert.Equal(2, compositions.Count);
        Assert.Null(compositions[1].LastMail.LastMailedAt);
        Assert.NotNull((await fixture.Outbox.GetCoverageAsync("anna", Stream)).LastMailedAt);
    }

    [Fact]
    public async Task AComposerThatThrows_KeepsTheOccurrencesWaiting_AndIsTriedAgainLater()
    {
        var calls = 0;
        var notice = Channels(Week, (_, _, _) =>
        {
            if (++calls == 1) throw new InvalidOperationException("the host's database is down");

            return Task.FromResult(new UserNoticeComposed { Subject = "Channels", BodyHtml = "<p>x</p>", CoveredSubjects = ["channel-1"] });
        });
        var fixture = Fixture(notice);

        await Raise(fixture, notice, "channel-1");
        await Pass(fixture, TimeSpan.FromMinutes(3));

        Assert.Empty(fixture.Queued);
        Assert.Equal(UserNoticeItemStatus.Waiting, Assert.Single(fixture.Outbox.Items).Status);

        await Pass(fixture, TimeSpan.FromMinutes(20));

        Assert.Single(fixture.Queued);
        Assert.Equal(2, calls);
        Assert.Equal(UserNoticeItemStatus.Mailed, Assert.Single(fixture.Outbox.Items).Status);
    }

    [Fact]
    public async Task ANoticeWithAnInterval_StillHonoursTheUsersChoiceAndTheCategory()
    {
        var notice = Channels(Week);
        var fixture = Fixture(notice);
        fixture.Store.Choices[("anna", notice.Key)] = new UserNoticeSetting { Enabled = false };

        await Raise(fixture, notice, "channel-1");
        await Pass(fixture, TimeSpan.FromMinutes(3));

        Assert.Empty(fixture.Queued);
        Assert.Empty(fixture.Outbox.Items);
    }

    [Fact]
    public async Task ANoticeWithNeither_IsUnchanged_MailedAsOftenAsOccurrencesArrive()
    {
        var notice = Channels();
        var fixture = Fixture(notice);

        await Raise(fixture, notice, "channel-1");
        await Pass(fixture, TimeSpan.FromMinutes(3));
        await Raise(fixture, notice, "channel-2");
        await Pass(fixture, TimeSpan.FromMinutes(3));

        Assert.Equal(2, fixture.Queued.Count);
    }
}
