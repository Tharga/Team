using Tharga.Team.Email.Users;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>A user's notice choices: offered only what they may receive, their choice over their team's default over the catalogue's, only a deviation stored, the delivery held to what the notice allows, and every change audited.</summary>
public class UserNoticePreferenceTests
{
    private readonly UserNoticeFixture _fixture = new();

    private static UserNoticeChoice Of(IEnumerable<UserNoticeChoice> choices, UserNoticeType notice) => choices.Single(x => x.Notice.Key == notice.Key);

    [Theory]
    [InlineData("eva", new[] { "case.assigned" })]
    [InlineData("anna", new[] { "case.assigned", "case.opened", "member.joined" })]
    [InlineData("bo", new[] { "case.assigned", "case.opened", "member.joined", "content.published" })]
    [InlineData("dan", new[] { "case.assigned", "case.opened" })]
    public async Task AUserIsOffered_TheNoticesTheyMayReceiveInAnyOfTheirTeams(string userKey, string[] expected)
    {
        var choices = await _fixture.Preferences(userKey).GetChoicesAsync(userKey);

        Assert.Equal(expected, choices.Select(x => x.Notice.Key));
    }

    [Fact]
    public async Task AUserInNoTeam_IsOfferedNothing()
    {
        Assert.Empty(await _fixture.Preferences("outsider").GetChoicesAsync("outsider"));
    }

    [Fact]
    public async Task WithNothingChosen_TheCataloguesDefaultApplies()
    {
        var choices = await _fixture.Preferences("bo").GetChoicesAsync("bo");

        var opened = Of(choices, UserNoticeFixture.Opened);
        Assert.True(opened.Enabled);
        Assert.Equal(UserNoticeDelivery.Digest, opened.Delivery);
        Assert.False(opened.EnabledChosen || opened.DeliveryChosen || opened.VariesByTeam);
        Assert.Equal(UserNoticeFixture.Work, opened.Category.Key);
        Assert.False(Of(choices, UserNoticeFixture.Published).Enabled);
    }

    [Fact]
    public async Task TheTeamsDefault_ReplacesTheCataloguesForItsMembers()
    {
        _fixture.Store.TeamDefaults[(UserNoticeFixture.Alpha, UserNoticeFixture.Opened.Key)] = new UserNoticeSetting { Enabled = false, Delivery = UserNoticeDelivery.AtOnce };

        var opened = Of(await _fixture.Preferences("cia").GetChoicesAsync("cia"), UserNoticeFixture.Opened);

        Assert.False(opened.Enabled);
        Assert.Equal(UserNoticeDelivery.AtOnce, opened.Delivery);
        Assert.False(opened.EnabledChosen);
    }

    [Fact]
    public async Task AUsersChoice_BeatsTheTeamsDefault_AndOnlyTheDeviatingPartIsStored()
    {
        _fixture.Store.TeamDefaults[(UserNoticeFixture.Alpha, UserNoticeFixture.Opened.Key)] = new UserNoticeSetting { Delivery = UserNoticeDelivery.AtOnce };

        var choice = await _fixture.Preferences("cia").SetChoiceAsync("cia", UserNoticeFixture.Opened.Key, true, UserNoticeDelivery.Digest);

        Assert.True(choice.Enabled);
        Assert.Equal(UserNoticeDelivery.Digest, choice.Delivery);
        Assert.True(choice.DeliveryChosen);
        Assert.False(choice.EnabledChosen);
        Assert.Equal(new UserNoticeSetting { Delivery = UserNoticeDelivery.Digest }, _fixture.Store.Choices[("cia", UserNoticeFixture.Opened.Key)]);
    }

    [Fact]
    public async Task ChoosingWhatTheDefaultsAlreadySay_StoresNothing()
    {
        var service = _fixture.Preferences("cia");
        await service.SetChoiceAsync("cia", UserNoticeFixture.Opened.Key, false, UserNoticeDelivery.AtOnce);

        var choice = await service.SetChoiceAsync("cia", UserNoticeFixture.Opened.Key, true, UserNoticeDelivery.Digest);

        Assert.False(choice.EnabledChosen || choice.DeliveryChosen);
        Assert.Empty(_fixture.Store.Choices);
    }

    [Fact]
    public async Task WhereTheUsersTeamsDisagree_TheyAreTold_AndAChoiceAgreeingWithOneIsStillStored()
    {
        _fixture.Store.TeamDefaults[(UserNoticeFixture.Beta, UserNoticeFixture.Assigned.Key)] = new UserNoticeSetting { Enabled = false };
        var service = _fixture.Preferences("anna");

        var before = Of(await service.GetChoicesAsync("anna"), UserNoticeFixture.Assigned);
        var chosen = await service.SetChoiceAsync("anna", UserNoticeFixture.Assigned.Key, true, UserNoticeDelivery.AtOnce);

        Assert.True(before.VariesByTeam);
        Assert.True(before.Enabled);
        Assert.False(chosen.VariesByTeam);
        Assert.True(chosen.EnabledChosen);
        Assert.Equal(new UserNoticeSetting { Enabled = true }, _fixture.Store.Choices[("anna", UserNoticeFixture.Assigned.Key)]);
    }

    [Fact]
    public async Task TheDeliveryChoice_IsHeldToWhatTheNoticeAllows()
    {
        var e = await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Preferences("anna").SetChoiceAsync("anna", UserNoticeFixture.Joined.Key, true, UserNoticeDelivery.Digest));

        Assert.Contains("member.joined", e.Message);
        Assert.Empty(_fixture.Store.Choices);
    }

    [Fact]
    public async Task ANoticeNotInTheCatalogue_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Preferences("anna").SetChoiceAsync("anna", "case.unknown", true, UserNoticeDelivery.AtOnce));
    }

    [Fact]
    public async Task ATurnedOffMailCategory_IsShownOnItsNotices()
    {
        await _fixture.Mail.Preferences.SetAsync("cia", _fixture.Mail.Categories.Find(UserNoticeFixture.Work), false, UserMailAudit.ChoiceAction, default);

        var choices = await _fixture.Preferences("cia").GetChoicesAsync("cia");

        Assert.All(choices, x => Assert.False(x.CategoryReceives));
    }

    [Fact]
    public async Task AChange_IsAudited_AndChoosingTheSameAgainIsNot()
    {
        var service = _fixture.Preferences("cia");

        await service.SetChoiceAsync("cia", UserNoticeFixture.Opened.Key, false, UserNoticeDelivery.Digest);
        await service.SetChoiceAsync("cia", UserNoticeFixture.Opened.Key, false, UserNoticeDelivery.Digest);

        var entry = Assert.Single(_fixture.Mail.Audit.Entries);
        Assert.Equal(AuditEventType.DataChange, entry.EventType);
        Assert.Equal(UserNoticeAudit.Feature, entry.Feature);
        Assert.Equal(UserNoticeAudit.ChoiceAction, entry.Action);
        Assert.True(entry.Success);
        Assert.Equal("cia", entry.Metadata[UserNoticeAudit.UserKey]);
        Assert.Equal(UserNoticeFixture.Opened.Key, entry.Metadata[UserNoticeAudit.Notice]);
        Assert.Equal("False", entry.Metadata[UserNoticeAudit.Enabled]);
        Assert.Equal("Digest", entry.Metadata[UserNoticeAudit.Delivery]);
    }

    [Fact]
    public async Task AnotherUsersChoices_AreRefused_AndTheAttemptAudited()
    {
        var service = _fixture.Preferences("cia");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetChoicesAsync("dan"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SetChoiceAsync("dan", UserNoticeFixture.Opened.Key, false, UserNoticeDelivery.Digest));

        Assert.Empty(_fixture.Store.Choices);
        var entry = Assert.Single(_fixture.Mail.Audit.Entries);
        Assert.False(entry.Success);
        Assert.Equal("dan", entry.Metadata[UserNoticeAudit.UserKey]);
    }

    [Fact]
    public async Task StaffWithUsersManage_ReadAndChangeAnyonesChoices()
    {
        var service = _fixture.Preferences("staff", SystemUserScopes.Manage);

        Assert.NotEmpty(await service.GetChoicesAsync("dan"));
        await service.SetChoiceAsync("dan", UserNoticeFixture.Opened.Key, false, UserNoticeDelivery.Digest);

        Assert.False(_fixture.Store.Choices[("dan", UserNoticeFixture.Opened.Key)].Enabled);
    }
}
