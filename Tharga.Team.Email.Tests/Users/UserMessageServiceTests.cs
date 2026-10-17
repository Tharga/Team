using Tharga.Team.Email.Users;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>Staff write to chosen users: who a selection reaches, which categories they may use, the counts before sending, one mail queued per user with the content stored once, and the sent log.</summary>
public class UserMessageServiceTests
{
    private static UserMessageRecipients Everyone(params string[] teams) => new() { TeamKeys = teams, EveryMember = true };

    private static async Task<string[]> ReachedAsync(UserMessageFixture fixture, UserMessageRecipients recipients)
    {
        await fixture.Service.SendAsync(UserMessageFixture.Message(recipients), TestContext.Current.CancellationToken);

        return [.. fixture.Mail.Outbox.Items.Select(x => x.Mail.RecipientUserKey).Order(StringComparer.Ordinal)];
    }

    [Fact]
    public async Task EveryoneInATeam_ReachesItsActiveMembers()
    {
        Assert.Equal(["anna", "bo", "cia"], await ReachedAsync(new UserMessageFixture(), Everyone(UserMessageFixture.Alpha)));
    }

    [Fact]
    public async Task SpecificUsers_AreReachedOnlyWithinTheChosenTeams()
    {
        var reached = await ReachedAsync(new UserMessageFixture(), new UserMessageRecipients { TeamKeys = [UserMessageFixture.Alpha], UserKeys = ["cia", "dan"] });

        Assert.Equal(["cia"], reached);
    }

    [Fact]
    public async Task ARole_ReachesItsHoldersInTheChosenTeams()
    {
        var reached = await ReachedAsync(new UserMessageFixture(), new UserMessageRecipients { TeamKeys = [UserMessageFixture.Alpha, UserMessageFixture.Beta], Roles = ["Editor"] });

        Assert.Equal(["anna", "bo"], reached);
    }

    [Fact]
    public async Task AnAccessLevel_AcrossAllTeams_ReachesItInEveryTeam()
    {
        var reached = await ReachedAsync(new UserMessageFixture(), new UserMessageRecipients { AllTeams = true, AccessLevels = [AccessLevel.Owner] });

        Assert.Equal(["anna", "dan"], reached);
    }

    [Fact]
    public async Task SelectionsCombine_AndEachUserIsReachedOnce()
    {
        var reached = await ReachedAsync(new UserMessageFixture(), new UserMessageRecipients
        {
            AllTeams = true,
            AccessLevels = [AccessLevel.Owner],
            Roles = ["Editor", "Auditor"],
            UserKeys = ["anna", "cia"]
        });

        Assert.Equal(["anna", "bo", "cia", "dan", "eva"], reached);
    }

    [Fact]
    public async Task EveryoneInEveryTeam_ReachesEachUserOnce()
    {
        Assert.Equal(["anna", "bo", "cia", "dan", "eva"], await ReachedAsync(new UserMessageFixture(), new UserMessageRecipients { AllTeams = true, EveryMember = true }));
    }

    [Fact]
    public async Task OnlyWhatTheDirectoryCountsAsActive_IsReached()
    {
        var fixture = new UserMessageFixture();
        fixture.Directory.Members.RemoveAll(x => x.UserKey == "bo");

        Assert.Equal(["anna", "cia"], await ReachedAsync(fixture, Everyone(UserMessageFixture.Alpha)));
    }

    [Fact]
    public async Task ASelectionNamingNoTeam_IsRefused()
    {
        var fixture = new UserMessageFixture();

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.PreviewAsync(new UserMessageRecipients { EveryMember = true }, UserMailFixture.News, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASelectionReachingNobody_IsRefused_AndNothingIsQueuedOrLogged()
    {
        var fixture = new UserMessageFixture();

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SendAsync(UserMessageFixture.Message(new UserMessageRecipients { TeamKeys = [UserMessageFixture.Alpha] }), TestContext.Current.CancellationToken));

        Assert.Empty(fixture.Mail.Outbox.Items);
        Assert.Empty(fixture.Log.Entries);
    }

    [Fact]
    public async Task ACategoryStaffMayNotUse_IsRefused()
    {
        var fixture = new UserMessageFixture();
        fixture.Options.StaffMessageCategories = [UserMailCategories.ServiceKey];

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.PreviewAsync(Everyone(UserMessageFixture.Alpha), UserMailFixture.News, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SendAsync(UserMessageFixture.Message(Everyone(UserMessageFixture.Alpha)), TestContext.Current.CancellationToken));

        Assert.Empty(fixture.Mail.Outbox.Items);
    }

    [Fact]
    public async Task TheChoices_OfferOnlyTheStaffCategories_InTheirOrder()
    {
        var fixture = new UserMessageFixture();
        fixture.Options.StaffMessageCategories = [UserMailFixture.News, UserMailCategories.ServiceKey];

        var choices = await fixture.Service.GetChoicesAsync(TestContext.Current.CancellationToken);

        Assert.Equal([UserMailFixture.News, UserMailCategories.ServiceKey], choices.Categories.Select(x => x.Key));
    }

    [Fact]
    public async Task TheChoices_ListTeamsUsersRolesAndLevels()
    {
        var choices = await new UserMessageFixture().Service.GetChoicesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Alpha", "Beta"], choices.Teams.Select(x => x.Name));
        Assert.Equal(["Anna", "Bo", "Cia", "Dan", "Eva"], choices.Users.Select(x => x.Name));
        Assert.Equal([UserMessageFixture.Alpha, UserMessageFixture.Beta], choices.Users.Single(x => x.UserKey == "anna").TeamKeys);
        Assert.Equal(["Auditor", "Editor"], choices.Roles);
        Assert.Equal([AccessLevel.Owner, AccessLevel.Administrator, AccessLevel.User, AccessLevel.Viewer], choices.AccessLevels);
        Assert.Equal(10, choices.Limits.MaxAttachmentCount);
    }

    [Fact]
    public async Task ThePreview_CountsThoseReached_AndThoseWhoTurnedTheCategoryOff()
    {
        var fixture = new UserMessageFixture();
        await fixture.Mail.Store.SetChoiceAsync("bo", UserMailFixture.News, false, TestContext.Current.CancellationToken);
        await fixture.Mail.Store.SetChoiceAsync("dan", UserMailFixture.News, false, TestContext.Current.CancellationToken);

        var preview = await fixture.Service.PreviewAsync(Everyone(UserMessageFixture.Alpha), UserMailFixture.News, TestContext.Current.CancellationToken);

        Assert.Equal(new UserMessagePreview { Recipients = 3, LeftOut = 1 }, preview);
    }

    [Fact]
    public async Task ThePreview_OfACategoryOffByDefault_LeavesOutAllWhoNeverTurnedItOn()
    {
        var fixture = new UserMessageFixture();
        await fixture.Mail.Store.SetChoiceAsync("anna", UserMailFixture.Digest, true, TestContext.Current.CancellationToken);

        var preview = await fixture.Service.PreviewAsync(Everyone(UserMessageFixture.Alpha), UserMailFixture.Digest, TestContext.Current.CancellationToken);

        Assert.Equal(new UserMessagePreview { Recipients = 3, LeftOut = 2 }, preview);
    }

    [Fact]
    public async Task ARequiredCategory_LeavesNobodyOut()
    {
        var fixture = new UserMessageFixture();
        await fixture.Mail.Store.SetChoiceAsync("anna", UserMailCategories.ServiceKey, false, TestContext.Current.CancellationToken);

        var preview = await fixture.Service.PreviewAsync(Everyone(UserMessageFixture.Alpha), UserMailCategories.ServiceKey, TestContext.Current.CancellationToken);

        Assert.Equal(new UserMessagePreview { Recipients = 3, LeftOut = 0 }, preview);
    }

    [Fact]
    public async Task Sending_QueuesOneMailPerUser_AndStoresTheContentAndFilesOnce()
    {
        var fixture = new UserMessageFixture();
        var file = new UserMailAttachment { FileName = "plan.pdf", ContentType = "application/pdf", Content = [1, 2, 3, 4] };

        var sent = await fixture.Service.SendAsync(UserMessageFixture.Message(Everyone(UserMessageFixture.Alpha, UserMessageFixture.Beta), attachments: [file]), TestContext.Current.CancellationToken);

        var batch = Assert.Single(fixture.Mail.Outbox.Batches);
        Assert.Equal(sent.Id, batch.Key);
        Assert.Equal("plan.pdf", Assert.Single(batch.Value.Attachments).FileName);
        Assert.Equal(5, fixture.Mail.Outbox.Items.Count);
        Assert.All(fixture.Mail.Outbox.Items, x =>
        {
            Assert.Equal(sent.Id, x.BatchId);
            Assert.Empty(x.Mail.Attachments);
        });
        Assert.Equal(5, sent.RecipientCount);
        Assert.Equal(new UserMailOutcomes { Queued = 5 }, sent.Outcomes);
    }

    [Fact]
    public async Task Sending_RecordsTheMessageInTheLog()
    {
        var fixture = new UserMessageFixture();

        var sent = await fixture.Service.SendAsync(UserMessageFixture.Message(new UserMessageRecipients
        {
            TeamKeys = [" team-alpha ", UserMessageFixture.Alpha],
            Roles = ["Editor"],
            AccessLevels = [AccessLevel.Owner, AccessLevel.Owner],
            UserKeys = ["cia"]
        }, attachments: [new UserMailAttachment { FileName = "a.txt", ContentType = "text/plain", Content = [1] }]), TestContext.Current.CancellationToken);

        var entry = Assert.Single(fixture.Log.Entries);
        Assert.Equal(sent.Id, entry.Id);
        Assert.Equal([UserMessageFixture.Alpha], entry.Recipients.TeamKeys);
        Assert.Equal(["Editor"], entry.Recipients.Roles);
        Assert.Equal([AccessLevel.Owner], entry.Recipients.AccessLevels);
        Assert.Equal(["cia"], entry.Recipients.UserKeys);
        Assert.Equal(UserMailFixture.News, entry.Category);
        Assert.Equal("Planned maintenance", entry.Subject);
        Assert.Equal(1, entry.AttachmentCount);
        Assert.Equal(UserMessageFixture.StaffKey, entry.SentByUserKey);
        Assert.Equal("Sam Staff", entry.SentByName);
        Assert.Equal(UserMailFixture.Start, entry.SentAt);
        Assert.Equal(3, entry.RecipientCount);
    }

    [Fact]
    public async Task EachMail_GoesFromTheHost_WithTheHostsReplyTo()
    {
        var fixture = new UserMessageFixture();
        await fixture.Service.SendAsync(UserMessageFixture.Message(Everyone(UserMessageFixture.Alpha)), TestContext.Current.CancellationToken);

        await fixture.Mail.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);

        Assert.Equal(3, fixture.Mail.Mail.Sent.Count);
        Assert.All(fixture.Mail.Mail.Sent, x => Assert.Equal("support@example.com", x.ReplyTo));
    }

    [Fact]
    public async Task WithoutAHostReplyTo_RepliesGoToTheSendingAddress()
    {
        var fixture = new UserMessageFixture(o => o.ReplyTo = null);
        await fixture.Service.SendAsync(UserMessageFixture.Message(Everyone(UserMessageFixture.Alpha)), TestContext.Current.CancellationToken);

        await fixture.Mail.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);

        Assert.All(fixture.Mail.Mail.Sent, x => Assert.Null(x.ReplyTo));
    }

    [Fact]
    public async Task TheOptOutIsCheckedAgainWhenEachMailIsSent()
    {
        var fixture = new UserMessageFixture();
        await fixture.Service.SendAsync(UserMessageFixture.Message(Everyone(UserMessageFixture.Alpha)), TestContext.Current.CancellationToken);
        await fixture.Mail.Store.SetChoiceAsync("bo", UserMailFixture.News, false, TestContext.Current.CancellationToken);

        await fixture.Mail.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);

        Assert.Equal(["anna@example.org", "cia@example.org"], fixture.Mail.Mail.Sent.Select(x => x.To.Split('<', '>')[1]).Order(StringComparer.Ordinal));
        Assert.Equal(UserMailStatus.Skipped, fixture.Mail.Outbox.Items.Single(x => x.Mail.RecipientUserKey == "bo").Status);
    }

    [Fact]
    public async Task TheSentLog_CountsEachMailsOutcome_NewestFirst()
    {
        var fixture = new UserMessageFixture();
        var first = await fixture.Service.SendAsync(UserMessageFixture.Message(Everyone(UserMessageFixture.Alpha)), TestContext.Current.CancellationToken);
        fixture.Mail.Time.Advance(TimeSpan.FromMinutes(5));
        var second = await fixture.Service.SendAsync(UserMessageFixture.Message(Everyone(UserMessageFixture.Beta)), TestContext.Current.CancellationToken);
        await fixture.Mail.Store.SetChoiceAsync("bo", UserMailFixture.News, false, TestContext.Current.CancellationToken);
        fixture.Mail.Recipients.Add(new UserMailRecipient { Key = "cia", Name = "Cia" });

        await fixture.Mail.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);
        var page = await fixture.Service.GetSentAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([second.Id, first.Id], page.Items.Select(x => x.Id));
        Assert.Equal(new UserMailOutcomes { Sent = 1, Skipped = 1, Failed = 1 }, page.Items[1].Outcomes);
        Assert.Equal(new UserMailOutcomes { Sent = 3 }, page.Items[0].Outcomes);
        Assert.All(page.Items, x => Assert.Empty(x.Failures));
    }

    [Fact]
    public async Task OneSentMessage_SaysWhyEachMailGivenUpWas_ByName()
    {
        var fixture = new UserMessageFixture();
        var sent = await fixture.Service.SendAsync(UserMessageFixture.Message(Everyone(UserMessageFixture.Alpha)), TestContext.Current.CancellationToken);
        fixture.Mail.Recipients.Add(new UserMailRecipient { Key = "cia", Name = "Cia" });

        await fixture.Mail.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);
        var message = await fixture.Service.GetSentMessageAsync(sent.Id, TestContext.Current.CancellationToken);

        var failure = Assert.Single(message.Failures);
        Assert.Equal("cia", failure.UserKey);
        Assert.Equal("Cia", failure.Name);
        Assert.Equal("The user record holds no address.", failure.Reason);
    }

    [Fact]
    public async Task AnUnknownMessage_IsNull()
    {
        Assert.Null(await new UserMessageFixture().Service.GetSentMessageAsync("nothing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Sending_IsAudited_WithCountsAndNeverAnAddress()
    {
        var fixture = new UserMessageFixture();

        var sent = await fixture.Service.SendAsync(UserMessageFixture.Message(Everyone(UserMessageFixture.Alpha, UserMessageFixture.Beta)), TestContext.Current.CancellationToken);

        var entry = Assert.Single(fixture.Mail.Audit.Entries);
        Assert.Equal(AuditEventType.DataChange, entry.EventType);
        Assert.Equal(UserMessageAudit.Feature, entry.Feature);
        Assert.Equal(UserMessageAudit.SendAction, entry.Action);
        Assert.True(entry.Success);
        Assert.Equal(sent.Id, entry.Metadata[UserMessageAudit.MessageId]);
        Assert.Equal("5", entry.Metadata[UserMessageAudit.Recipients]);
        Assert.Equal("2", entry.Metadata[UserMessageAudit.Teams]);
        Assert.Equal(UserMailFixture.News, entry.Metadata[UserMessageAudit.Category]);
        Assert.DoesNotContain(entry.Metadata.Values, x => x.Contains('@'));
    }

    [Fact]
    public async Task ARefusedSend_IsAuditedAsAFailure()
    {
        var fixture = new UserMessageFixture();

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SendAsync(UserMessageFixture.Message(Everyone(UserMessageFixture.Alpha), "tips"), TestContext.Current.CancellationToken));

        var entry = Assert.Single(fixture.Mail.Audit.Entries);
        Assert.Equal(UserMessageAudit.SendAction, entry.Action);
        Assert.False(entry.Success);
        Assert.Contains("tips", entry.ErrorMessage);
    }

    [Fact]
    public async Task ReadingTheLog_IsAudited_WithCounts()
    {
        var fixture = new UserMessageFixture();
        var sent = await fixture.Service.SendAsync(UserMessageFixture.Message(new UserMessageRecipients { AllTeams = true, EveryMember = true }), TestContext.Current.CancellationToken);
        fixture.Mail.Audit.Entries.Clear();

        await fixture.Service.GetSentAsync(cancellationToken: TestContext.Current.CancellationToken);
        await fixture.Service.GetSentMessageAsync(sent.Id, TestContext.Current.CancellationToken);

        Assert.Equal([UserMessageAudit.LogAction, UserMessageAudit.OpenAction], fixture.Mail.Audit.Entries.Select(x => x.Action));
        Assert.Equal("1", fixture.Mail.Audit.Entries[0].Metadata[UserMessageAudit.Count]);
        Assert.Equal("5", fixture.Mail.Audit.Entries[1].Metadata[UserMessageAudit.Queued]);
        Assert.All(fixture.Mail.Audit.Entries.SelectMany(x => x.Metadata.Values), x => Assert.DoesNotContain("@", x));
    }

    [Fact]
    public async Task AMessageThatCannotBeRecorded_IsStillQueued_AndTheCallerIsTold()
    {
        var fixture = new UserMessageFixture();
        fixture.Log.FailWith = new InvalidOperationException("The log is down.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SendAsync(UserMessageFixture.Message(Everyone(UserMessageFixture.Alpha)), TestContext.Current.CancellationToken));

        Assert.Equal(3, fixture.Mail.Outbox.Items.Count);
        Assert.False(Assert.Single(fixture.Mail.Audit.Entries).Success);
    }
}
