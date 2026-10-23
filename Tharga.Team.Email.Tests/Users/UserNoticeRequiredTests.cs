using Tharga.Team.Email.Users;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>A required notice: sent to everyone it admits whatever a user or their team chose, only its delivery left to choose, and kept in a category no one can turn off.</summary>
public class UserNoticeRequiredTests
{
    private static readonly UserNoticeType Agreement = new()
    {
        Key = "terms.changed",
        Name = "The terms of use change",
        Category = UserMailCategories.AccountKey,
        Audience = UserNoticeAudience.AtLeast(AccessLevel.Administrator),
        Required = true,
        OnByDefault = false,
        Deliveries = [UserNoticeDelivery.AtOnce, UserNoticeDelivery.Digest],
        Subject = "{subject}: the terms of use change"
    };

    private readonly UserNoticeFixture _fixture = new(o =>
    {
        o.Add(UserNoticeFixture.Assigned);
        o.Add(Agreement);
    });

    private static readonly UserNoticeSetting Off = new() { Enabled = false };

    private static UserNoticeChoice Of(IEnumerable<UserNoticeChoice> choices) => choices.Single(x => x.Notice.Key == Agreement.Key);

    private async Task AdvanceAndRunAsync(TimeSpan by)
    {
        _fixture.Mail.Time.Advance(by);
        await _fixture.RunAsync();
    }

    [Fact]
    public void ARequiredNotice_IsKept_WhenItIsSentInACategoryNoOneCanTurnOff()
    {
        var registry = UserNoticeRegistry.Create([Agreement], _fixture.Mail.Categories);

        Assert.True(registry.Find(Agreement.Key).Required);
    }

    [Fact]
    public void ARequiredNotice_InACategoryAUserCanTurnOff_IsRefused()
    {
        var e = Assert.Throws<InvalidOperationException>(() => UserNoticeRegistry.Create([Agreement with { Category = UserNoticeFixture.Work }], _fixture.Mail.Categories));

        Assert.Contains(Agreement.Key, e.Message);
        Assert.Contains($"'{UserNoticeFixture.Work}'", e.Message);
    }

    [Fact]
    public async Task ARequiredNotice_ReachesEveryRecipient_WhateverTheyOrTheirTeamChose()
    {
        _fixture.Store.Choices[("anna", Agreement.Key)] = Off;
        _fixture.Store.TeamDefaults[(UserNoticeFixture.Alpha, Agreement.Key)] = Off;

        await _fixture.RaiseAsync(Agreement, "terms-2");
        await AdvanceAndRunAsync(TimeSpan.FromMinutes(2));

        Assert.Equal(["anna", "bo"], _fixture.Outbox.Items.Select(x => x.UserKey).Order(StringComparer.Ordinal));
        Assert.All(_fixture.Outbox.Items, x => Assert.Equal(UserNoticeItemStatus.Mailed, x.Status));
        Assert.Equal(["anna", "bo"], _fixture.Queued.Select(x => x.Mail.RecipientUserKey).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ARequiredNotice_TurnedOffWhileItWaits_IsStillMailed()
    {
        await _fixture.RaiseAsync(Agreement, "terms-2");
        await _fixture.RunAsync();
        _fixture.Store.Choices[("bo", Agreement.Key)] = Off;
        _fixture.Store.TeamDefaults[(UserNoticeFixture.Alpha, Agreement.Key)] = Off;

        await AdvanceAndRunAsync(TimeSpan.FromMinutes(2));

        Assert.DoesNotContain(_fixture.Outbox.Items, x => x.Status == UserNoticeItemStatus.Skipped);
        Assert.Equal(2, _fixture.Queued.Count);
    }

    [Fact]
    public async Task TurningARequiredNoticeOff_IsRefused_AndNothingIsStoredOrAudited()
    {
        var e = await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Preferences("anna").SetChoiceAsync("anna", Agreement.Key, false, UserNoticeDelivery.AtOnce));

        Assert.Contains(Agreement.Key, e.Message);
        Assert.Equal("enabled", e.ParamName);
        Assert.Empty(_fixture.Store.Choices);
        Assert.Empty(_fixture.Mail.Audit.Entries);
    }

    [Fact]
    public async Task ARequiredNotice_IsShownAsOn_EvenWhereAnEarlierChoiceOrDefaultTurnedItOff()
    {
        _fixture.Store.Choices[("anna", Agreement.Key)] = Off;
        _fixture.Store.TeamDefaults[(UserNoticeFixture.Alpha, Agreement.Key)] = Off;

        var choice = Of(await _fixture.Preferences("anna").GetChoicesAsync("anna"));

        Assert.True(choice.Enabled);
        Assert.False(choice.EnabledChosen);
        Assert.False(choice.VariesByTeam);
    }

    [Fact]
    public async Task TheDeliveryOfARequiredNotice_IsStillTheUsersChoice()
    {
        var choice = await _fixture.Preferences("anna").SetChoiceAsync("anna", Agreement.Key, true, UserNoticeDelivery.Digest);

        Assert.True(choice.Enabled);
        Assert.Equal(UserNoticeDelivery.Digest, choice.Delivery);
        Assert.True(choice.DeliveryChosen);
        Assert.False(choice.EnabledChosen);
        Assert.Equal(new UserNoticeSetting { Delivery = UserNoticeDelivery.Digest }, _fixture.Store.Choices[("anna", Agreement.Key)]);

        await _fixture.RaiseAsync(Agreement, "terms-2");
        await _fixture.RunAsync();

        Assert.Equal(UserNoticeDelivery.Digest, _fixture.Outbox.Items.Single(x => x.UserKey == "anna").Delivery);
        Assert.Equal(UserNoticeDelivery.AtOnce, _fixture.Outbox.Items.Single(x => x.UserKey == "bo").Delivery);
    }

    [Fact]
    public async Task TurningARequiredNoticeOffForATeam_IsRefused_AndNothingIsStored()
    {
        var e = await Assert.ThrowsAsync<ArgumentException>(() => _fixture.TeamDefaults.SetTeamDefaultAsync(UserNoticeFixture.Alpha, Agreement.Key, false, UserNoticeDelivery.AtOnce));

        Assert.Contains(Agreement.Key, e.Message);
        Assert.Equal("enabled", e.ParamName);
        Assert.Empty(_fixture.Store.TeamDefaults);
        Assert.Empty(_fixture.Mail.Audit.Entries);
    }

    [Fact]
    public async Task ATeamsDefaultForARequiredNotice_IsOn_AndOnlyItsDeliveryCanBeSet()
    {
        var service = _fixture.TeamDefaults;
        _fixture.Store.TeamDefaults[(UserNoticeFixture.Alpha, Agreement.Key)] = Off;

        var before = (await service.GetTeamDefaultsAsync(UserNoticeFixture.Alpha)).Single(x => x.Notice.Key == Agreement.Key);
        var set = await service.SetTeamDefaultAsync(UserNoticeFixture.Alpha, Agreement.Key, true, UserNoticeDelivery.Digest);

        Assert.True(before.Enabled);
        Assert.False(before.IsSet);
        Assert.True(set.Enabled);
        Assert.True(set.IsSet);
        Assert.Equal(UserNoticeDelivery.Digest, set.Delivery);
        Assert.Equal(new UserNoticeSetting { Delivery = UserNoticeDelivery.Digest }, _fixture.Store.TeamDefaults[(UserNoticeFixture.Alpha, Agreement.Key)]);
    }

    [Fact]
    public async Task ANoticeThatIsNotRequired_StillCanBeTurnedOff()
    {
        var choice = await _fixture.Preferences("cia").SetChoiceAsync("cia", UserNoticeFixture.Assigned.Key, false, UserNoticeDelivery.AtOnce);

        Assert.False(choice.Enabled);
        Assert.True(choice.EnabledChosen);
    }
}
