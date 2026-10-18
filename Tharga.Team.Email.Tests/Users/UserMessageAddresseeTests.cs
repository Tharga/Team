using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MimeKit;
using Tharga.Team.Email.Users;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>
/// The list of who a message reaches before it is sent: a filter, users added whatever it says, or both, each user
/// once with how they came in and whether they turned the category off; the same users the counts count and sending
/// queues, read a page at a time.
/// </summary>
public partial class UserMessageAddresseeTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static UserMessageRecipients Everyone(params string[] teams) => new() { TeamKeys = teams, EveryMember = true };

    private static async Task<UserMessageAddressee[]> ListAsync(UserMessageFixture fixture, UserMessageRecipients selection, string category = UserMailFixture.News)
        => (await fixture.Service.GetAddresseesAsync(selection, category, 0, 100, None)).Items;

    [Fact]
    public async Task TheFilterAlone_ListsTheTeamsMatches_ByName_WithTheirAddressAndTeam()
    {
        var list = await ListAsync(new UserMessageFixture(), Everyone(UserMessageFixture.Alpha));

        Assert.Equal(["Anna", "Bo", "Cia"], list.Select(x => x.Name));
        Assert.Equal(["anna@example.org", "bo@example.org", "cia@example.org"], list.Select(x => x.EMail));
        Assert.All(list, x =>
        {
            Assert.Equal([UserMessageFixture.Alpha], x.TeamKeys);
            Assert.True(x.ByFilter);
            Assert.False(x.Added);
            Assert.False(x.OptedOut);
        });
    }

    [Fact]
    public async Task UsersAddedAlone_AreAValidSelection_ListedWithEveryTeamTheyAreActiveIn()
    {
        var list = await ListAsync(new UserMessageFixture(), new UserMessageRecipients { UserKeys = ["anna"] });

        var anna = Assert.Single(list);
        Assert.Equal([UserMessageFixture.Alpha, UserMessageFixture.Beta], anna.TeamKeys);
        Assert.True(anna.Added);
        Assert.False(anna.ByFilter);
    }

    [Fact]
    public async Task UsersAddedAlone_AreSent()
    {
        var fixture = new UserMessageFixture();

        var sent = await fixture.Service.SendAsync(UserMessageFixture.Message(new UserMessageRecipients { UserKeys = ["dan", "eva"] }), None);

        Assert.Equal(2, sent.RecipientCount);
        Assert.Equal(["dan", "eva"], fixture.Mail.Outbox.Items.Select(x => x.Mail.RecipientUserKey).Order(StringComparer.Ordinal));
        Assert.Equal(["dan", "eva"], Assert.Single(fixture.Log.Entries).Recipients.UserKeys);
    }

    [Fact]
    public async Task TheFilterAndUsersAdded_Combine_AndAUserInBothIsListedOnce_AsBoth()
    {
        var list = await ListAsync(new UserMessageFixture(), new UserMessageRecipients { TeamKeys = [UserMessageFixture.Alpha], EveryMember = true, UserKeys = ["anna", "dan", "dan"] });

        Assert.Equal(["anna", "bo", "cia", "dan"], list.Select(x => x.UserKey));
        var anna = list.Single(x => x.UserKey == "anna");
        Assert.True(anna.ByFilter && anna.Added);
        Assert.Equal([UserMessageFixture.Alpha, UserMessageFixture.Beta], anna.TeamKeys);
        var dan = list.Single(x => x.UserKey == "dan");
        Assert.True(dan.Added && !dan.ByFilter);
    }

    [Fact]
    public async Task AUserAdded_WhoIsActiveInNoTeam_IsNotReached()
    {
        var fixture = new UserMessageFixture();
        fixture.Mail.Recipients.Add(new UserMailRecipient { Key = "gone", EMail = "gone@example.org", Name = "Gone" });

        var preview = await fixture.Service.PreviewAsync(new UserMessageRecipients { UserKeys = ["gone"] }, UserMailFixture.News, None);

        Assert.Equal(0, preview.Recipients);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SendAsync(UserMessageFixture.Message(new UserMessageRecipients { UserKeys = ["gone"] }), None));
    }

    [Fact]
    public async Task ASelectionNamingNeitherTeamsNorUsers_IsRefused()
    {
        var fixture = new UserMessageFixture();

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.GetAddresseesAsync(new UserMessageRecipients { EveryMember = true, UserKeys = [" "] }, UserMailFixture.News, 0, 10, None));
    }

    [Fact]
    public async Task ThoseWhoTurnedTheCategoryOff_AreMarked_AndCountedLeftOut()
    {
        var fixture = new UserMessageFixture();
        await fixture.Mail.Store.SetChoiceAsync("bo", UserMailFixture.News, false, None);

        var page = await fixture.Service.GetAddresseesAsync(Everyone(UserMessageFixture.Alpha), UserMailFixture.News, 0, 10, None);

        Assert.Equal(["bo"], page.Items.Where(x => x.OptedOut).Select(x => x.UserKey));
        Assert.Equal((3, 1), (page.Total, page.LeftOut));
    }

    [Fact]
    public async Task InARequiredCategory_NobodyIsMarkedOptedOut()
    {
        var fixture = new UserMessageFixture();
        await fixture.Mail.Store.SetChoiceAsync("bo", UserMailCategories.ServiceKey, false, None);

        var list = await ListAsync(fixture, Everyone(UserMessageFixture.Alpha), UserMailCategories.ServiceKey);

        Assert.DoesNotContain(list, x => x.OptedOut);
    }

    [Fact]
    public async Task TheList_IsReadAPageAtATime_InOneOrder_WithAPageCappedAtAHundred()
    {
        var fixture = new UserMessageFixture();
        for (var i = 0; i < 230; i++) fixture.Member(UserMessageFixture.Beta, $"user-{i:D3}", $"Person {i:D3}", AccessLevel.User);

        var first = await fixture.Service.GetAddresseesAsync(Everyone(UserMessageFixture.Beta), UserMailFixture.News, 0, 25, None);
        var second = await fixture.Service.GetAddresseesAsync(Everyone(UserMessageFixture.Beta), UserMailFixture.News, 25, 25, None);
        var capped = await fixture.Service.GetAddresseesAsync(Everyone(UserMessageFixture.Beta), UserMailFixture.News, 0, 1000, None);
        var beyond = await fixture.Service.GetAddresseesAsync(Everyone(UserMessageFixture.Beta), UserMailFixture.News, 500, 25, None);

        Assert.Equal(233, first.Total);
        Assert.Equal(25, first.Items.Length);
        Assert.Equal(capped.Items.Skip(25).Take(25).Select(x => x.UserKey), second.Items.Select(x => x.UserKey));
        Assert.Empty(first.Items.Select(x => x.UserKey).Intersect(second.Items.Select(x => x.UserKey)));
        Assert.Equal(100, capped.Items.Length);
        Assert.Empty(beyond.Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task TheList_TheCounts_AndWhatIsQueued_AreTheSameUsers(int index)
    {
        var selection = Selections[index];
        var fixture = new UserMessageFixture();
        await fixture.Mail.Store.SetChoiceAsync("dan", UserMailFixture.News, false, None);

        var page = await fixture.Service.GetAddresseesAsync(selection, UserMailFixture.News, 0, 100, None);
        var preview = await fixture.Service.PreviewAsync(selection, UserMailFixture.News, None);
        await fixture.Service.SendAsync(UserMessageFixture.Message(selection), None);

        Assert.Equal((preview.Recipients, preview.LeftOut), (page.Total, page.LeftOut));
        Assert.Equal(page.Items.Select(x => x.UserKey).Order(StringComparer.Ordinal), fixture.Mail.Outbox.Items.Select(x => x.Mail.RecipientUserKey).Order(StringComparer.Ordinal));
    }

    private static readonly UserMessageRecipients[] Selections =
    [
        new UserMessageRecipients { TeamKeys = [UserMessageFixture.Alpha], EveryMember = true },
        new UserMessageRecipients { UserKeys = ["dan", "cia"] },
        new UserMessageRecipients { AllTeams = true, Roles = ["Editor"], UserKeys = ["eva", "anna"] },
        new UserMessageRecipients { TeamKeys = [UserMessageFixture.Beta], AccessLevels = [AccessLevel.Owner], UserKeys = ["dan", "bo"] }
    ];

    [Fact]
    public async Task TheAddressShown_IsTheOneOnTheUserRecord_WhichSendingUses()
    {
        var fixture = new UserMessageFixture();
        fixture.Mail.Recipients.Add(new UserMailRecipient { Key = "cia", EMail = "cia.new@example.org", Name = "Cia" });

        var list = await ListAsync(fixture, Everyone(UserMessageFixture.Alpha));

        Assert.Equal("cia.new@example.org", list.Single(x => x.UserKey == "cia").EMail);
    }

    [Fact]
    public async Task UsersAdded_AreReadOnlyByTheirKeys_AndTheFilterIsNotReadWhenItReachesNobody()
    {
        var fixture = new UserMessageFixture();
        var directory = Substitute.For<IUserMessageMemberDirectory>();
        directory.GetActiveMembershipsOfUsersAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>()).Returns([new UserMessageMember { TeamKey = "t", UserKey = "dan", Name = "Dan" }]);
        fixture.Members = directory;

        var preview = await fixture.Service.PreviewAsync(new UserMessageRecipients { TeamKeys = ["t"], UserKeys = ["dan"] }, UserMailFixture.News, None);

        Assert.Equal(1, preview.Recipients);
        await directory.Received(1).GetActiveMembershipsOfUsersAsync(Arg.Is<string[]>(x => x.SequenceEqual(new[] { "dan" })), Arg.Any<CancellationToken>());
        await directory.DidNotReceiveWithAnyArgs().GetActiveMembersAsync(default, default);
        await directory.DidNotReceiveWithAnyArgs().GetActiveMembersOfEveryTeamAsync(default);
    }

    [Fact]
    public async Task TheDirectoryDefault_FindsTheUsersAmongEveryTeamsActiveMembers()
    {
        IUserMessageMemberDirectory directory = new UserMessageFixture().Directory;

        var found = await directory.GetActiveMembershipsOfUsersAsync(["anna", "eva", "nobody", null], None);

        Assert.Equal([("anna", UserMessageFixture.Alpha), ("anna", UserMessageFixture.Beta), ("eva", UserMessageFixture.Beta)], found.Select(x => (x.UserKey, x.TeamKey)));
    }

    [Fact]
    public async Task ThePreview_IsTheMailARecipientReceives_FromTheHost_WithTheFooter()
    {
        var fixture = new UserMessageFixture();

        var preview = await fixture.Service.PreviewMailAsync(UserMessageFixture.Message(Everyone(UserMessageFixture.Alpha)), None);

        Assert.Equal(("Example", "hello@example.com"), (MailboxAddress.Parse(preview.From).Name, MailboxAddress.Parse(preview.From).Address));
        Assert.Equal("support@example.com", preview.ReplyTo);
        Assert.Null(preview.To);
        Assert.Equal("Planned maintenance", preview.Subject);
        Assert.StartsWith("<p>We will be <b>down</b> on Sunday.</p><hr><p>", preview.Html);
        Assert.Contains($"token={UserMailFooter.PreviewToken}", preview.Html);
        Assert.StartsWith("We will be down on Sunday.\n\n-- \n", preview.Text);
    }

    [Fact]
    public async Task ThePreview_OfARequiredCategory_HasNoFooter()
    {
        var preview = await new UserMessageFixture().Service.PreviewMailAsync(UserMessageFixture.Message(Everyone(UserMessageFixture.Alpha), UserMailCategories.ServiceKey), None);

        Assert.Equal("<p>We will be <b>down</b> on Sunday.</p>", preview.Html);
        Assert.Equal("We will be down on Sunday.", preview.Text);
    }

    [Fact]
    public async Task ThePreview_OfACategoryStaffMayNotUse_IsRefused()
    {
        var fixture = new UserMessageFixture();
        fixture.Options.StaffMessageCategories = [UserMailCategories.ServiceKey];

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.PreviewMailAsync(UserMessageFixture.Message(Everyone(UserMessageFixture.Alpha)), None));
    }

    [Fact]
    public async Task ThePreview_StoresNothing_AndSendsNothing()
    {
        var fixture = new UserMessageFixture();

        await fixture.Service.PreviewMailAsync(UserMessageFixture.Message(Everyone(UserMessageFixture.Alpha)), None);

        Assert.Empty(fixture.Mail.Outbox.Items);
        Assert.Empty(fixture.Log.Entries);
        Assert.Empty(fixture.Mail.Mail.Sent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThePreview_EqualsTheMailSent_ButForTheRecipientsOwnUnsubscribeToken(bool withTemplate)
    {
        var fixture = new UserMessageFixture();
        if (withTemplate) fixture.Mail.Template = new MailTemplate(Options.Create(new MailTemplateOptions { Html = "<body><h1>{{subject}}</h1>{{content}}<small>{{footer}}</small></body>" }));
        var message = UserMessageFixture.Message(new UserMessageRecipients { UserKeys = ["cia"] });

        var preview = await fixture.Service.PreviewMailAsync(message, None);
        await fixture.Service.SendAsync(message, None);
        await fixture.Mail.Dispatcher().RunAsync("me", None);

        var sent = Assert.Single(fixture.Mail.Mail.Sent);
        Assert.Equal(preview.Subject, sent.Subject);
        Assert.Equal(preview.Html, Token().Replace(sent.BodyHtml, $"token={UserMailFooter.PreviewToken}"));
        Assert.Equal(preview.Text, Token().Replace(sent.Body, $"token={UserMailFooter.PreviewToken}"));
    }

    [GeneratedRegex(@"token=[^\s""&<]+")]
    private static partial Regex Token();
}
