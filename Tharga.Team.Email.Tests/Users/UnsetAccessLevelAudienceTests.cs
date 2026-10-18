using Tharga.Team.Email.Users;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>
/// A member with no access level holds nothing since 4.0, roles and overrides included, so a message or notice that
/// selects by level, role or scope never reaches them; one written to every member, or naming them, still does.
/// </summary>
public class UnsetAccessLevelAudienceTests
{
    private const string Unset = "gus";

    private static UserMessageFixture Messages()
    {
        var fixture = new UserMessageFixture();
        fixture.Member(UserMessageFixture.Alpha, Unset, "Gus", null, "Editor");
        return fixture;
    }

    private static async Task<string[]> ReachedAsync(UserMessageFixture fixture, UserMessageRecipients recipients)
    {
        await fixture.Service.SendAsync(UserMessageFixture.Message(recipients), TestContext.Current.CancellationToken);

        return [.. fixture.Mail.Outbox.Items.Select(x => x.Mail.RecipientUserKey).Order(StringComparer.Ordinal)];
    }

    [Fact]
    public async Task AMessageToEveryAccessLevel_DoesNotReachAMemberWithNone()
    {
        var reached = await ReachedAsync(Messages(), new UserMessageRecipients { TeamKeys = [UserMessageFixture.Alpha], AccessLevels = Enum.GetValues<AccessLevel>() });

        Assert.Equal(["anna", "bo", "cia"], reached);
    }

    [Fact]
    public async Task AMessageToARole_DoesNotReachAMemberWithNoLevel_WhoHoldsIt()
    {
        var reached = await ReachedAsync(Messages(), new UserMessageRecipients { TeamKeys = [UserMessageFixture.Alpha], Roles = ["Editor"] });

        Assert.Equal(["bo"], reached);
    }

    [Fact]
    public async Task AMessageToEveryMember_ReachesAMemberWithNoLevel()
    {
        var reached = await ReachedAsync(Messages(), new UserMessageRecipients { TeamKeys = [UserMessageFixture.Alpha], EveryMember = true });

        Assert.Equal(["anna", "bo", "cia", Unset], reached);
    }

    [Fact]
    public async Task AMessageNamingAMemberWithNoLevel_ReachesThem()
    {
        var reached = await ReachedAsync(Messages(), new UserMessageRecipients { TeamKeys = [UserMessageFixture.Alpha], UserKeys = [Unset] });

        Assert.Equal([Unset], reached);
    }

    [Fact]
    public async Task TheChoices_OfferNoRoleOnlyAMemberWithNoLevelHolds()
    {
        var fixture = Messages();
        fixture.Member(UserMessageFixture.Beta, "hal", "Hal", null, "Ghost");

        var choices = await fixture.Service.GetChoicesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Auditor", "Editor"], choices.Roles);
        Assert.Equal([AccessLevel.Owner, AccessLevel.Administrator, AccessLevel.User, AccessLevel.Viewer], choices.AccessLevels);
    }

    private static UserNoticeFixture Notices()
    {
        var fixture = new UserNoticeFixture();
        fixture.Member(UserNoticeFixture.Alpha, Unset, null, [UserNoticeFixture.Editor], [UserNoticeFixture.CaseRead]);
        return fixture;
    }

    private static UserNoticeOccurrence Occurrence(UserNoticeType notice, string[] recipients = null) => new()
    {
        NoticeKey = notice.Key,
        TeamKey = UserNoticeFixture.Alpha,
        SubjectId = "1",
        Recipients = recipients ?? []
    };

    private static UserNoticeType ToEveryMember => new()
    {
        Key = "team.news",
        Name = "News for the team",
        Category = UserNoticeFixture.Work,
        Audience = UserNoticeAudience.EveryMember,
        Subject = "{subject}"
    };

    [Fact]
    public async Task ANoticeByScope_RoleOrLevel_DoesNotReachAMemberWithNoLevel_ThoughTheyHoldTheRoleAndTheOverride()
    {
        var distributor = Notices().Distributor();
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(["anna", "bo", "cia", "dan"], await distributor.RecipientsAsync(UserNoticeFixture.Opened, Occurrence(UserNoticeFixture.Opened), ct));
        Assert.Equal(["bo"], await distributor.RecipientsAsync(UserNoticeFixture.Published, Occurrence(UserNoticeFixture.Published), ct));
        Assert.Equal(["anna", "bo"], await distributor.RecipientsAsync(UserNoticeFixture.Joined, Occurrence(UserNoticeFixture.Joined), ct));
    }

    [Fact]
    public async Task ANoticeToEveryMember_OrNamingThem_ReachesAMemberWithNoLevel()
    {
        var distributor = Notices().Distributor();
        var ct = TestContext.Current.CancellationToken;

        Assert.Contains(Unset, await distributor.RecipientsAsync(ToEveryMember, Occurrence(ToEveryMember), ct));
        Assert.Equal([Unset], await distributor.RecipientsAsync(UserNoticeFixture.Assigned, Occurrence(UserNoticeFixture.Assigned, [Unset]), ct));
        Assert.Contains(Unset, await distributor.RecipientsAsync(UserNoticeFixture.Joined, Occurrence(UserNoticeFixture.Joined, [Unset]), ct));
    }

    [Fact]
    public async Task AMemberWithNoLevel_IsOfferedOnlyTheNoticesTheyCouldBeSent()
    {
        var choices = await Notices().Preferences(Unset).GetChoicesAsync(Unset);

        Assert.Equal([UserNoticeFixture.Assigned.Key], choices.Select(x => x.Notice.Key));
    }

    [Fact]
    public async Task WithNoScopeRegistry_AMemberWithNoLevel_HoldsNotEvenTheirOverrides()
    {
        var member = new UserMessageMember { TeamKey = UserNoticeFixture.Alpha, UserKey = Unset, AccessLevel = null, ScopeOverrides = [UserNoticeFixture.CaseRead] };

        Assert.False(await new UserNoticeEligibility().MayReceiveAsync(UserNoticeFixture.Opened, member));
        Assert.True(await new UserNoticeEligibility().MayReceiveAsync(UserNoticeFixture.Opened, member with { AccessLevel = AccessLevel.Viewer }));
    }

    [Fact]
    public async Task AHostsRoleService_IsNotAskedForTheScopesOfAMemberWithNoLevel()
    {
        var roles = Substitute.For<ITenantRoleService>();
        roles.GetEffectiveScopesAsync(default, default, default, default).ReturnsForAnyArgs([UserNoticeFixture.CaseRead]);
        var member = new UserMessageMember { TeamKey = UserNoticeFixture.Alpha, UserKey = Unset, AccessLevel = null };

        Assert.False(await new UserNoticeEligibility(roles).MayReceiveAsync(UserNoticeFixture.Opened, member));
        await roles.DidNotReceiveWithAnyArgs().GetEffectiveScopesAsync(default, default, default, default);
    }

    [Fact]
    public void TheAudienceRules_AdmitANullLevel_OnlyForEveryMemberAndNamedOrResolvedNotices()
    {
        Assert.False(UserNoticeAudience.WithScope("case:read").MayReceive(null, [], ["case:read"]));
        Assert.False(UserNoticeAudience.WithRole("Editor").MayReceive(null, ["Editor"], []));
        Assert.All(Enum.GetValues<AccessLevel>(), level => Assert.False(UserNoticeAudience.AtLeast(level).MayReceive(null, [], [])));
        Assert.True(UserNoticeAudience.EveryMember.MayReceive(null, [], []));
        Assert.True(UserNoticeAudience.NamedOnly.MayReceive(null, [], []));
        Assert.True(UserNoticeAudience.ResolvedBy((_, _, _) => Task.FromResult<IEnumerable<string>>([])).MayReceive(null, [], []));
    }
}
