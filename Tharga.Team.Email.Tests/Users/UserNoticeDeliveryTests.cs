using Tharga.Team.Email.Users;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>
/// Delivery: who an occurrence reaches, a notice at once through the user mail queue after the burst window, a burst as
/// one mail, the daily summary once per user and day whichever instance sends it, the same occurrence to the same
/// person once, and the user's choice, the team's default and the mail category all honoured.
/// </summary>
public class UserNoticeDeliveryTests
{
    private readonly UserNoticeFixture _fixture = new();

    private string[] ItemUsers(string noticeKey = null)
        => [.. _fixture.Outbox.Items.Where(x => noticeKey == null || x.NoticeKey == noticeKey).Select(x => x.UserKey).Order(StringComparer.Ordinal)];

    private async Task AdvanceAndRunAsync(TimeSpan by, string claimant = "instance-a")
    {
        _fixture.Mail.Time.Advance(by);
        await _fixture.RunAsync(claimant);
    }

    [Fact]
    public async Task TheRule_AndTheNamedRecipients_AreUnited_AmongTheTeamsActiveMembers()
    {
        await _fixture.RaiseAsync(UserNoticeFixture.Joined, "new-member", recipients: ["cia", "outsider", "fia"]);
        await _fixture.RunAsync();

        Assert.Equal(["anna", "bo", "cia"], ItemUsers());
        Assert.Equal(3, Assert.Single(_fixture.Outbox.Records).Recipients);
        Assert.Equal(UserNoticeStatus.Distributed, _fixture.Outbox.Records[0].Status);
    }

    [Fact]
    public async Task AScopeRule_ReadsEachMembersEffectiveScopes_IncludingOverrides()
    {
        await _fixture.RaiseAsync(UserNoticeFixture.Opened, "1");
        await _fixture.RunAsync();

        Assert.Equal(["anna", "bo", "cia", "dan"], ItemUsers());
    }

    [Fact]
    public async Task ARoleRule_AndAnAccessLevelRule_AreReadInTheOccurrencesTeam()
    {
        var distributor = _fixture.Distributor();

        Assert.Equal(["bo"], await distributor.RecipientsAsync(UserNoticeFixture.Published, Occurrence(UserNoticeFixture.Published, UserNoticeFixture.Alpha), default));
        Assert.Equal(["fia"], await distributor.RecipientsAsync(UserNoticeFixture.Published, Occurrence(UserNoticeFixture.Published, UserNoticeFixture.Beta), default));
        Assert.Equal(["anna", "bo"], await distributor.RecipientsAsync(UserNoticeFixture.Joined, Occurrence(UserNoticeFixture.Joined, UserNoticeFixture.Alpha), default));
        Assert.Equal(["fia"], await distributor.RecipientsAsync(UserNoticeFixture.Joined, Occurrence(UserNoticeFixture.Joined, UserNoticeFixture.Beta), default));
    }

    [Fact]
    public async Task ANamedOnlyNotice_ReachesOnlyThoseNamed()
    {
        var distributor = _fixture.Distributor();

        Assert.Empty(await distributor.RecipientsAsync(UserNoticeFixture.Assigned, Occurrence(UserNoticeFixture.Assigned, UserNoticeFixture.Alpha), default));
        Assert.Equal(["eva"], await distributor.RecipientsAsync(UserNoticeFixture.Assigned, Occurrence(UserNoticeFixture.Assigned, UserNoticeFixture.Alpha) with { Recipients = ["eva", "fia"] }, default));
    }

    [Fact]
    public async Task AResolvedRule_AsksTheHostPerOccurrence_AndKeepsOnlyActiveMembers()
    {
        UserNoticeOccurrence asked = null;
        var resolved = UserNoticeFixture.Assigned with
        {
            Audience = UserNoticeAudience.ResolvedBy((occurrence, services, _) =>
            {
                asked = occurrence;
                Assert.NotNull(services.GetService(typeof(UserNoticeFixture)));
                return Task.FromResult<IEnumerable<string>>(["eva", "outsider", "fia"]);
            })
        };
        var fixture = new UserNoticeFixture(o => o.Add(resolved));

        await fixture.RaiseAsync(resolved, "7", recipients: ["cia"]);
        await fixture.RunAsync();

        Assert.Equal("7", asked.SubjectId);
        Assert.Equal(["cia", "eva"], fixture.Outbox.Items.Select(x => x.UserKey).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ANoticeAtOnce_WaitsTheBurstWindow_ThenGoesThroughTheUserMailQueue()
    {
        await _fixture.RaiseAsync(UserNoticeFixture.Assigned, "1", recipients: ["cia"]);
        await _fixture.RunAsync();

        Assert.Equal(UserNoticeItemStatus.Waiting, Assert.Single(_fixture.Outbox.Items).Status);
        Assert.Empty(_fixture.Queued);

        await AdvanceAndRunAsync(TimeSpan.FromMinutes(2));

        var queued = _fixture.QueuedTo("cia");
        Assert.Equal(UserNoticeFixture.Work, queued.Mail.Category);
        Assert.Equal("Case 1 was assigned to you", queued.Mail.Subject);
        Assert.Contains("Bo assigned <b>Case 1</b> to you.", queued.Mail.BodyHtml);
        Assert.Contains("https://app.example.com/cases/1", queued.Mail.BodyHtml);
        Assert.Equal(UserNoticeItemStatus.Mailed, _fixture.Outbox.Items[0].Status);
        Assert.Equal(queued.Id, _fixture.Outbox.Items[0].MailId);

        await _fixture.Mail.Dispatcher().RunAsync("sender", default);

        var sent = Assert.Single(_fixture.Mail.Mail.Sent);
        Assert.Contains("cia@example.org", sent.To);
        Assert.NotNull(sent.UnsubscribeUrl);
    }

    [Fact]
    public async Task ABurstOfTheSameNotice_IsOneMail_AndTheNextBurstAnother()
    {
        await _fixture.RaiseAsync(UserNoticeFixture.Assigned, "1", recipients: ["cia"]);
        await _fixture.RunAsync();
        await AdvanceAndRunAsync(TimeSpan.FromSeconds(30));
        await _fixture.RaiseAsync(UserNoticeFixture.Assigned, "2", recipients: ["cia"]);
        await AdvanceAndRunAsync(TimeSpan.FromSeconds(60));
        await _fixture.RaiseAsync(UserNoticeFixture.Assigned, "3", recipients: ["cia"]);
        await _fixture.RunAsync();

        Assert.Empty(_fixture.Queued);

        await AdvanceAndRunAsync(TimeSpan.FromSeconds(30));

        var mail = Assert.Single(_fixture.Queued).Mail;
        Assert.Equal(UserMailKind.Notice, mail.Kind);
        Assert.Equal("A case is assigned to me (3)", mail.Subject);
        Assert.Contains("https://app.example.com/cases/1", mail.BodyHtml);
        Assert.Contains("Case 3 was assigned to you", mail.BodyHtml);

        await _fixture.RaiseAsync(UserNoticeFixture.Assigned, "4", recipients: ["cia"]);
        await AdvanceAndRunAsync(TimeSpan.FromMinutes(1));
        Assert.Single(_fixture.Queued);

        await AdvanceAndRunAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(2, _fixture.Queued.Count);
        Assert.Equal("Case 4 was assigned to you", _fixture.Queued[1].Mail.Subject);
    }

    [Fact]
    public async Task WithNoBurstWindow_ANoticeGoesOnTheNextPass()
    {
        var fixture = new UserNoticeFixture(o =>
        {
            o.BurstWindow = TimeSpan.Zero;
            o.Add(UserNoticeFixture.Assigned);
        });

        await fixture.RaiseAsync(UserNoticeFixture.Assigned, "1", recipients: ["cia"]);
        await fixture.RunAsync();

        Assert.Single(fixture.Queued);
    }

    [Fact]
    public async Task TheDailySummary_ListsAUsersNotices_GroupedByNotice_AtTheSummaryTime()
    {
        _fixture.Store.Choices[("cia", UserNoticeFixture.Assigned.Key)] = new UserNoticeSetting { Delivery = UserNoticeDelivery.Digest };
        await _fixture.RaiseAsync(UserNoticeFixture.Opened, "1");
        await _fixture.RaiseAsync(UserNoticeFixture.Opened, "2");
        await _fixture.RaiseAsync(UserNoticeFixture.Assigned, "3", recipients: ["cia"]);
        await _fixture.RunAsync();
        await AdvanceAndRunAsync(TimeSpan.FromHours(6));

        Assert.Empty(_fixture.Queued);

        await AdvanceAndRunAsync(UserNoticeFixture.DigestDue - _fixture.Mail.Time.GetUtcNow().UtcDateTime);

        Assert.Equal(["anna", "bo", "cia", "dan"], _fixture.Queued.Select(x => x.Mail.RecipientUserKey).Order(StringComparer.Ordinal));
        var cia = _fixture.QueuedTo("cia").Mail;
        Assert.Equal("Your daily summary (3)", cia.Subject);
        Assert.Equal(UserNoticeFixture.Work, cia.Category);
        Assert.Contains("<h3>A case is opened in my team</h3>", cia.BodyHtml);
        Assert.Contains("<h3>A case is assigned to me</h3>", cia.BodyHtml);
        Assert.Contains("New case: Case 2", cia.BodyHtml);
        Assert.True(cia.BodyHtml.IndexOf("A case is assigned to me", StringComparison.Ordinal) < cia.BodyHtml.IndexOf("A case is opened in my team", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ANoticeArrivingAfterTodaysSummary_WaitsForTomorrows()
    {
        await _fixture.RaiseAsync(UserNoticeFixture.Opened, "1");
        await _fixture.RunAsync();
        await AdvanceAndRunAsync(UserNoticeFixture.DigestDue - UserMailFixture.Start);
        var sent = _fixture.Queued.Count;
        Assert.Equal(4, sent);

        await _fixture.RaiseAsync(UserNoticeFixture.Opened, "2");
        await AdvanceAndRunAsync(TimeSpan.FromHours(1));
        Assert.Equal(sent, _fixture.Queued.Count);

        await AdvanceAndRunAsync(TimeSpan.FromHours(23));
        Assert.Equal(sent * 2, _fixture.Queued.Count);
    }

    [Fact]
    public async Task TheDailySummary_OnTwoInstances_IsSentOncePerUser()
    {
        for (var i = 0; i < 10; i++) await _fixture.RaiseAsync(UserNoticeFixture.Opened, $"{i}");
        await _fixture.RunAsync();
        _fixture.Mail.Time.Advance(UserNoticeFixture.DigestDue - UserMailFixture.Start);

        await Task.WhenAll(
            Task.Run(() => _fixture.RunAsync("instance-a")),
            Task.Run(() => _fixture.RunAsync("instance-b")));

        Assert.Equal(["anna", "bo", "cia", "dan"], _fixture.Queued.Select(x => x.Mail.RecipientUserKey).Order(StringComparer.Ordinal));
        Assert.All(_fixture.Queued, x => Assert.Equal("Your daily summary (10)", x.Mail.Subject));
        Assert.Equal(40, _fixture.Outbox.Items.Count(x => x.Status == UserNoticeItemStatus.Mailed));
    }

    [Fact]
    public async Task WhenTodaysSummaryWasAlreadyQueued_WhatWaitsGoesInTheNextOne()
    {
        await _fixture.RaiseAsync(UserNoticeFixture.Opened, "1", teamKey: UserNoticeFixture.Alpha);
        await _fixture.RunAsync();
        _fixture.Mail.Time.Advance(UserNoticeFixture.DigestDue - UserMailFixture.Start);
        var key = UserNoticeMail.MailKey("cia", UserNoticeMail.DigestStream(UserNoticeFixture.Work), DateOnly.FromDateTime(UserNoticeFixture.DigestDue));
        await _fixture.Mail.Queue.QueueAsync(new UserMail { RecipientUserKey = "cia", Category = UserNoticeFixture.Work, Subject = "Earlier", Body = "Earlier", DeduplicationKey = key });

        await _fixture.RunAsync();

        Assert.Equal(UserNoticeItemStatus.Waiting, _fixture.Outbox.Items.Single(x => x.UserKey == "cia").Status);
        Assert.Equal(UserNoticeFixture.DigestDue.AddDays(1), _fixture.Outbox.StreamOf("cia", UserNoticeMail.DigestStream(UserNoticeFixture.Work))?.DueAt);
        Assert.Equal(UserNoticeItemStatus.Mailed, _fixture.Outbox.Items.Single(x => x.UserKey == "anna").Status);
    }

    [Fact]
    public async Task AMailBegunByAnInstanceThatStopped_IsFinished_WithoutASecondMail()
    {
        await _fixture.RaiseAsync(UserNoticeFixture.Assigned, "1", recipients: ["cia"]);
        await _fixture.RunAsync();
        _fixture.Mail.Time.Advance(TimeSpan.FromMinutes(2));
        var now = _fixture.Mail.Time.GetUtcNow().UtcDateTime;
        var stream = UserNoticeMail.AtOnceStream(UserNoticeFixture.Assigned.Key);

        var claim = await _fixture.Outbox.ClaimDueStreamAsync("stopped", now, TimeSpan.FromMinutes(5));
        var items = await _fixture.Outbox.GetWaitingAsync("cia", stream, 10);
        await _fixture.Outbox.BeginMailAsync("cia", stream, "stopped", "notice:begun", [.. items.Select(x => x.Id)]);
        await _fixture.Mail.Queue.QueueAsync(await _fixture.NoticeMail().ComposeAsync("cia", stream, items) with { DeduplicationKey = "notice:begun" });
        _fixture.Outbox.ExpireStreamClaims();

        await _fixture.RunAsync("instance-b");

        Assert.Equal("cia", claim.UserKey);
        Assert.Single(_fixture.Queued);
        Assert.Equal(UserNoticeItemStatus.Mailed, Assert.Single(_fixture.Outbox.Items).Status);
        Assert.Null(_fixture.Outbox.StreamOf("cia", stream)?.MailKey);
    }

    [Fact]
    public async Task TheSameOccurrenceRaisedAgain_ReachesEachPersonOnce_AndOnlyThoseNewToIt()
    {
        await _fixture.RaiseAsync(UserNoticeFixture.Assigned, "1", recipients: ["cia"], discriminator: "first");
        await _fixture.RunAsync();
        await _fixture.RaiseAsync(UserNoticeFixture.Assigned, "1", recipients: ["cia", "dan"], discriminator: "first");
        await _fixture.RunAsync();
        await AdvanceAndRunAsync(TimeSpan.FromMinutes(2));
        await _fixture.RaiseAsync(UserNoticeFixture.Assigned, "1", recipients: ["cia"], discriminator: "first");
        await AdvanceAndRunAsync(TimeSpan.FromMinutes(2));

        Assert.Equal(["cia", "dan"], ItemUsers());
        Assert.Equal(["cia", "dan"], _fixture.Queued.Select(x => x.Mail.RecipientUserKey).Order(StringComparer.Ordinal));
        Assert.Equal([1, 1, 0], _fixture.Outbox.Records.Select(x => x.Recipients));
    }

    [Fact]
    public async Task TheSameOccurrenceRaisedOnTwoInstances_ReachesEachPersonOnce()
    {
        await _fixture.RaiseAsync(UserNoticeFixture.Opened, "1", discriminator: "opened");
        await _fixture.RaiseAsync(UserNoticeFixture.Opened, "1", discriminator: "opened");

        await Task.WhenAll(
            Task.Run(() => _fixture.RunAsync("instance-a")),
            Task.Run(() => _fixture.RunAsync("instance-b")));

        Assert.Equal(["anna", "bo", "cia", "dan"], ItemUsers());
    }

    [Fact]
    public async Task AnotherDiscriminator_IsAnotherOccurrence()
    {
        await _fixture.RaiseAsync(UserNoticeFixture.Assigned, "1", recipients: ["cia"], discriminator: "14-days-before");
        await _fixture.RaiseAsync(UserNoticeFixture.Assigned, "1", recipients: ["cia"], discriminator: "1-day-before");
        await _fixture.RunAsync();

        Assert.Equal(2, _fixture.Outbox.Items.Count);
    }

    [Fact]
    public async Task AUserWhoTurnedTheNoticeOff_IsNotReached()
    {
        _fixture.Store.Choices[("anna", UserNoticeFixture.Joined.Key)] = new UserNoticeSetting { Enabled = false };

        await _fixture.RaiseAsync(UserNoticeFixture.Joined, "new");
        await _fixture.RunAsync();

        Assert.Equal(["bo"], ItemUsers());
    }

    [Fact]
    public async Task TheTeamsDefault_AppliesToMembersWhoHaveNotChosen_AndAUsersChoiceBeatsIt()
    {
        _fixture.Store.TeamDefaults[(UserNoticeFixture.Alpha, UserNoticeFixture.Opened.Key)] = new UserNoticeSetting { Delivery = UserNoticeDelivery.AtOnce };
        _fixture.Store.TeamDefaults[(UserNoticeFixture.Alpha, UserNoticeFixture.Joined.Key)] = new UserNoticeSetting { Enabled = false };
        _fixture.Store.Choices[("cia", UserNoticeFixture.Opened.Key)] = new UserNoticeSetting { Delivery = UserNoticeDelivery.Digest };
        _fixture.Store.Choices[("bo", UserNoticeFixture.Joined.Key)] = new UserNoticeSetting { Enabled = true };

        await _fixture.RaiseAsync(UserNoticeFixture.Opened, "1");
        await _fixture.RaiseAsync(UserNoticeFixture.Joined, "new");
        await _fixture.RunAsync();

        var opened = _fixture.Outbox.Items.Where(x => x.NoticeKey == UserNoticeFixture.Opened.Key).ToDictionary(x => x.UserKey, x => x.Delivery);
        Assert.Equal(UserNoticeDelivery.AtOnce, opened["anna"]);
        Assert.Equal(UserNoticeDelivery.Digest, opened["cia"]);
        Assert.Equal(["bo"], ItemUsers(UserNoticeFixture.Joined.Key));
    }

    [Fact]
    public async Task AStoredDeliveryTheNoticeNoLongerAllows_FallsBackToTheDefault()
    {
        _fixture.Store.Choices[("anna", UserNoticeFixture.Joined.Key)] = new UserNoticeSetting { Delivery = UserNoticeDelivery.Digest };

        await _fixture.RaiseAsync(UserNoticeFixture.Joined, "new");
        await _fixture.RunAsync();

        Assert.Equal(UserNoticeDelivery.AtOnce, _fixture.Outbox.Items.Single(x => x.UserKey == "anna").Delivery);
    }

    [Fact]
    public async Task ANoticeTurnedOffWhileItWaits_IsNotMailed()
    {
        await _fixture.RaiseAsync(UserNoticeFixture.Opened, "1");
        await _fixture.RaiseAsync(UserNoticeFixture.Assigned, "2", recipients: ["cia"]);
        await _fixture.RunAsync();
        _fixture.Store.Choices[("cia", UserNoticeFixture.Opened.Key)] = new UserNoticeSetting { Enabled = false };
        _fixture.Store.Choices[("cia", UserNoticeFixture.Assigned.Key)] = new UserNoticeSetting { Delivery = UserNoticeDelivery.Digest };

        await AdvanceAndRunAsync(UserNoticeFixture.DigestDue - UserMailFixture.Start);

        var opened = _fixture.Outbox.Items.Single(x => x.UserKey == "cia" && x.NoticeKey == UserNoticeFixture.Opened.Key);
        Assert.Equal(UserNoticeItemStatus.Skipped, opened.Status);
        Assert.Equal(UserNoticeDistributor.TurnedOffReason, opened.Reason);
        Assert.Equal(UserNoticeItemStatus.Mailed, _fixture.Outbox.Items.Single(x => x.NoticeKey == UserNoticeFixture.Assigned.Key).Status);
        Assert.Equal("Case 2 was assigned to you", _fixture.QueuedTo("cia").Mail.Subject);
        Assert.Equal(4, _fixture.Queued.Count);
    }

    [Fact]
    public async Task TurningTheMailCategoryOff_StopsTheNotice_WhenItIsSent()
    {
        await _fixture.Mail.Preferences.SetAsync("cia", _fixture.Mail.Categories.Find(UserNoticeFixture.Work), false, UserMailAudit.ChoiceAction, default);

        await _fixture.RaiseAsync(UserNoticeFixture.Assigned, "1", recipients: ["cia"]);
        await AdvanceAndRunAsync(TimeSpan.Zero);
        await AdvanceAndRunAsync(TimeSpan.FromMinutes(2));
        await _fixture.Mail.Dispatcher().RunAsync("sender", default);

        Assert.Equal(UserMailStatus.Skipped, _fixture.QueuedTo("cia").Status);
        Assert.Empty(_fixture.Mail.Mail.Sent);
    }

    [Fact]
    public async Task ADistributionThatFails_IsTriedAgainLater_AndGivenUp()
    {
        var failing = UserNoticeFixture.Assigned with { Audience = UserNoticeAudience.ResolvedBy((_, _, _) => throw new TimeoutException("The directory is down.")) };
        var fixture = new UserNoticeFixture(o =>
        {
            o.MaxAttempts = 2;
            o.Add(failing);
        });

        await fixture.RaiseAsync(failing, "1");
        await fixture.RunAsync();

        var record = Assert.Single(fixture.Outbox.Records);
        Assert.Equal(UserNoticeStatus.Pending, record.Status);
        Assert.Equal("The directory is down.", record.Reason);
        Assert.Equal(UserMailFixture.Start.AddMinutes(1), record.NextAttemptAt);

        fixture.Mail.Time.Advance(TimeSpan.FromMinutes(1));
        await fixture.RunAsync();

        Assert.Equal(UserNoticeStatus.Failed, fixture.Outbox.Records[0].Status);
        Assert.StartsWith("Gave up after 2 attempts.", fixture.Outbox.Records[0].Reason);
    }

    [Fact]
    public async Task AnOccurrenceOfANoticeNoLongerInTheCatalogue_IsGivenUp()
    {
        await _fixture.RaiseAsync(UserNoticeFixture.Joined, "new");

        await _fixture.Distributor(notices: UserNoticeRegistry.Create([UserNoticeFixture.Opened], _fixture.Mail.Categories)).RunAsync("instance-a", default);

        Assert.Equal(UserNoticeStatus.Failed, _fixture.Outbox.Records[0].Status);
        Assert.Equal(UserNoticeDistributor.NotInCatalogueReason, _fixture.Outbox.Records[0].Reason);
    }

    [Fact]
    public async Task TheMail_IsWrittenInTheHostsLanguage_FallingBackToEnglishForATranslationThatDoesNotFill()
    {
        var texts = new DictionaryTextResolver(new Dictionary<string, string>
        {
            [UserNoticeFixture.Assigned.SubjectKey] = "{subject} tilldelades dig av {someone}",
            [UserNoticeFixture.Assigned.BodyKey] = "<p>{actor} gav dig <b>{subject}</b>.</p>",
            [UserNoticeTexts.OpenKey] = "Öppna"
        });

        await _fixture.RaiseAsync(UserNoticeFixture.Assigned, "1", recipients: ["cia"]);
        await _fixture.RunAsync(texts: texts);
        _fixture.Mail.Time.Advance(TimeSpan.FromMinutes(2));
        await _fixture.RunAsync(texts: texts);

        var mail = _fixture.QueuedTo("cia").Mail;
        Assert.Equal("Case 1 was assigned to you", mail.Subject);
        Assert.Contains("Bo gav dig <b>Case 1</b>.", mail.BodyHtml);
        Assert.Contains(">Öppna</a>", mail.BodyHtml);
    }

    private static UserNoticeOccurrence Occurrence(UserNoticeType notice, string teamKey) => new()
    {
        NoticeKey = notice.Key,
        TeamKey = teamKey,
        SubjectId = "1"
    };
}
