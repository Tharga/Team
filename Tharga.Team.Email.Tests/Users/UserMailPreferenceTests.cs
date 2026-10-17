using System.Security.Claims;
using Tharga.Team.Email.Users;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>A user's choice per category: the default until they choose, only a deviation stored, required categories always on, every change audited and only their own changeable.</summary>
public class UserMailPreferenceTests
{
    private const string Other = "user-2";

    private readonly UserMailFixture _fixture = new();

    private UserMailPreferenceService Service(string currentUser = UserMailFixture.UserKey, params string[] systemScopes)
    {
        var users = Substitute.For<IUserService>();
        var user = Substitute.For<IUser>();
        user.Key.Returns(currentUser);
        users.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(currentUser == null ? null : user);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(systemScopes.Select(x => new Claim(TeamClaimTypes.SystemScope, x)), "Cookies"));
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(principal));

        return new UserMailPreferenceService(_fixture.Preferences, _fixture.Categories, new TeamAuthorizer(accessor), users);
    }

    [Fact]
    public async Task AUserWhoNeverChose_GetsEachCategorysDefault()
    {
        var choices = await Service().GetChoicesAsync(UserMailFixture.UserKey);

        Assert.Equal(["account", "service", "news", "digest"], choices.Select(x => x.Category.Key));
        Assert.All(choices, x => Assert.True(x.IsDefault));
        Assert.True(choices.Single(x => x.Category.Key == UserMailFixture.News).Receives);
        Assert.False(choices.Single(x => x.Category.Key == UserMailFixture.Digest).Receives);
    }

    [Fact]
    public async Task ReadingOneChoice_IsOneCall()
    {
        var service = Service();

        Assert.True(await service.ReceivesAsync(UserMailFixture.UserKey, UserMailFixture.News));
        Assert.False(await service.ReceivesAsync(UserMailFixture.UserKey, UserMailFixture.Digest));
        Assert.True(await service.ReceivesAsync(UserMailFixture.UserKey, UserMailCategories.AccountKey));
    }

    [Fact]
    public async Task TurningACategoryOff_IsStored_AndRead()
    {
        var service = Service();

        var choice = await service.SetChoiceAsync(UserMailFixture.UserKey, UserMailFixture.News, false);

        Assert.False(choice.Receives);
        Assert.False(choice.IsDefault);
        Assert.False(_fixture.Store.Stored[(UserMailFixture.UserKey, UserMailFixture.News)]);
        Assert.False(await service.ReceivesAsync(UserMailFixture.UserKey, UserMailFixture.News));
    }

    [Fact]
    public async Task ChoosingTheDefault_StoresNothing()
    {
        var service = Service();
        await service.SetChoiceAsync(UserMailFixture.UserKey, UserMailFixture.News, false);

        var choice = await service.SetChoiceAsync(UserMailFixture.UserKey, UserMailFixture.News, true);
        await service.SetChoiceAsync(UserMailFixture.UserKey, UserMailFixture.Digest, false);

        Assert.True(choice.IsDefault);
        Assert.Empty(_fixture.Store.Stored);
    }

    [Fact]
    public async Task AnOffByDefaultCategory_TurnedOn_IsTheStoredDeviation()
    {
        await Service().SetChoiceAsync(UserMailFixture.UserKey, UserMailFixture.Digest, true);

        Assert.True(_fixture.Store.Stored[(UserMailFixture.UserKey, UserMailFixture.Digest)]);
    }

    [Fact]
    public async Task ARequiredCategory_IgnoresAStoredOptOut()
    {
        await _fixture.Store.SetChoiceAsync(UserMailFixture.UserKey, UserMailCategories.AccountKey, false);

        var choices = await Service().GetChoicesAsync(UserMailFixture.UserKey);

        Assert.True(choices.Single(x => x.Category.Key == UserMailCategories.AccountKey).Receives);
        Assert.True(await Service().ReceivesAsync(UserMailFixture.UserKey, UserMailCategories.AccountKey));
    }

    [Fact]
    public async Task ARequiredCategory_CannotBeTurnedOff()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().SetChoiceAsync(UserMailFixture.UserKey, UserMailCategories.ServiceKey, false));

        Assert.Equal(0, _fixture.Store.Writes);
    }

    [Fact]
    public async Task AnUnknownCategory_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service().SetChoiceAsync(UserMailFixture.UserKey, "nope", false));
        await Assert.ThrowsAsync<ArgumentException>(() => Service().ReceivesAsync(UserMailFixture.UserKey, "nope"));
    }

    [Fact]
    public async Task AChange_IsAudited_WithWhoseChoiceWhichCategoryAndWhatItIsNow()
    {
        await Service().SetChoiceAsync(UserMailFixture.UserKey, UserMailFixture.News, false);

        var entry = Assert.Single(_fixture.Audit.Entries);
        Assert.Equal(AuditEventType.DataChange, entry.EventType);
        Assert.Equal(UserMailAudit.Feature, entry.Feature);
        Assert.Equal(UserMailAudit.ChoiceAction, entry.Action);
        Assert.True(entry.Success);
        Assert.Equal(UserMailFixture.UserKey, entry.Metadata[UserMailAudit.UserKey]);
        Assert.Equal(UserMailFixture.News, entry.Metadata[UserMailAudit.Category]);
        Assert.Equal("False", entry.Metadata[UserMailAudit.Receives]);
    }

    [Fact]
    public async Task ChoosingWhatIsAlreadyInForce_IsNotAudited()
    {
        await Service().SetChoiceAsync(UserMailFixture.UserKey, UserMailFixture.News, true);

        Assert.Empty(_fixture.Audit.Entries);
    }

    [Fact]
    public async Task AnotherUsersChoices_AreRefused_AndTheAttemptIsAudited()
    {
        var service = Service(currentUser: Other);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetChoicesAsync(UserMailFixture.UserKey));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReceivesAsync(UserMailFixture.UserKey, UserMailFixture.News));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SetChoiceAsync(UserMailFixture.UserKey, UserMailFixture.News, false));

        Assert.Equal(0, _fixture.Store.Writes);
        var entry = Assert.Single(_fixture.Audit.Entries);
        Assert.False(entry.Success);
        Assert.Equal(UserMailFixture.UserKey, entry.Metadata[UserMailAudit.UserKey]);
    }

    [Fact]
    public async Task NobodySignedIn_IsRefused()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(currentUser: null).GetChoicesAsync(UserMailFixture.UserKey));
    }

    [Fact]
    public async Task StaffWithUsersManage_MayReadAndChangeAnyUsersChoices()
    {
        var service = Service(Other, SystemUserScopes.Manage);

        await service.SetChoiceAsync(UserMailFixture.UserKey, UserMailFixture.News, false);

        Assert.False(await service.ReceivesAsync(UserMailFixture.UserKey, UserMailFixture.News));
        Assert.False(_fixture.Store.Stored[(UserMailFixture.UserKey, UserMailFixture.News)]);
    }

    [Fact]
    public async Task AScopeOnTheSelectedTeamOnly_DoesNotStandInForTheSystemScope()
    {
        var users = Substitute.For<IUserService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(TeamClaimTypes.Scope, SystemUserScopes.Manage)], "Cookies"));
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(principal));
        var service = new UserMailPreferenceService(_fixture.Preferences, _fixture.Categories, new TeamAuthorizer(accessor), users);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetChoicesAsync(UserMailFixture.UserKey));
    }
}
