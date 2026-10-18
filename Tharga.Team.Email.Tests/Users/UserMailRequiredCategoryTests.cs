using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tharga.Team.Email.Users;
using Tharga.Team.Service;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>
/// Exactly one category can never be turned off when the host wants it so: account is always required, service messages
/// can be made optional and on by default, staff may write in account to reach everyone, and a host names where its
/// terms of use are for the "always sent" line.
/// </summary>
public class UserMailRequiredCategoryTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static UserMessageRecipients Everyone(params string[] teams) => new() { TeamKeys = teams, EveryMember = true };

    [Fact]
    public void ByDefault_ServiceMessagesAreRequired()
    {
        var registry = UserMailCategoryRegistry.Create([]);

        Assert.True(new UserMailOptions().ServiceRequired);
        Assert.True(registry.Find(UserMailCategories.ServiceKey).Required);
        Assert.False(registry.HasOptional);
    }

    [Fact]
    public void ServiceMessagesMadeOptional_AreOnByDefault_AndAccountStaysRequired()
    {
        var registry = UserMailCategoryRegistry.Create([], serviceRequired: false);

        var service = registry.Find(UserMailCategories.ServiceKey);
        Assert.False(service.Required);
        Assert.True(service.OnByDefault);
        Assert.Equal(UserMailCategories.Service.NameKey, service.NameKey);
        Assert.True(registry.Find(UserMailCategories.AccountKey).Required);
        Assert.True(registry.HasOptional);
        Assert.True(UserMailCategories.Service.Required);
    }

    [Fact]
    public void ServiceMessagesMadeOptional_NeedABaseUrl_ForTheirUnsubscribeLinks()
    {
        var e = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddThargaUserMail(o => o.ServiceRequired = false));

        Assert.Contains(nameof(UserMailOptions.BaseUrl), e.Message);
    }

    [Fact]
    public async Task OptionalServiceMessages_CarryAnUnsubscribeLink_AndAUserWhoTurnedThemOffIsSkipped()
    {
        var fixture = new UserMailFixture(o => o.ServiceRequired = false);

        await fixture.QueueAsync(UserMailCategories.ServiceKey);
        await fixture.Dispatcher().RunAsync("me", None);
        await fixture.Preferences.SetAsync(UserMailFixture.UserKey, fixture.Categories.Find(UserMailCategories.ServiceKey), false, UserMailAudit.ChoiceAction, None);
        await fixture.QueueAsync(UserMailCategories.ServiceKey);
        await fixture.Dispatcher().RunAsync("me", None);

        var sent = Assert.Single(fixture.Mail.Sent);
        Assert.StartsWith("https://app.example.com/mail/unsubscribe?token=", sent.UnsubscribeUrl);
        Assert.Equal(UserMailStatus.Skipped, fixture.Outbox.Items[^1].Status);
    }

    [Fact]
    public async Task RequiredServiceMessages_CarryNoUnsubscribeLink_AndReachAUserWhateverIsStored()
    {
        var fixture = new UserMailFixture();
        await fixture.Store.SetChoiceAsync(UserMailFixture.UserKey, UserMailCategories.ServiceKey, false, None);

        await fixture.QueueAsync(UserMailCategories.ServiceKey);
        await fixture.Dispatcher().RunAsync("me", None);

        Assert.Null(Assert.Single(fixture.Mail.Sent).UnsubscribeUrl);
    }

    [Fact]
    public void StaffMayBeAllowedToWriteInTheAccountCategory()
    {
        var services = new ServiceCollection();
        services.AddThargaUserMail();

        services.AddThargaUserMessages(o => o.StaffMessageCategories = [UserMailCategories.AccountKey, UserMailCategories.ServiceKey]);
    }

    [Fact]
    public async Task AStaffMessageInTheAccountCategory_ReachesEveryUser_WithNoUnsubscribeLink()
    {
        var fixture = new UserMessageFixture();
        fixture.Options.StaffMessageCategories = [UserMailCategories.AccountKey];
        await fixture.Mail.Store.SetChoiceAsync("bo", UserMailCategories.AccountKey, false, None);

        var choices = await fixture.Service.GetChoicesAsync(None);
        var preview = await fixture.Service.PreviewAsync(Everyone(UserMessageFixture.Alpha), UserMailCategories.AccountKey, None);
        await fixture.Service.SendAsync(UserMessageFixture.Message(Everyone(UserMessageFixture.Alpha), UserMailCategories.AccountKey), None);
        await fixture.Mail.Dispatcher().RunAsync("me", None);

        Assert.Equal([UserMailCategories.AccountKey], choices.Categories.Select(x => x.Key));
        Assert.Equal(new UserMessagePreview { Recipients = 3, LeftOut = 0 }, preview);
        Assert.Equal(3, fixture.Mail.Mail.Sent.Count);
        Assert.All(fixture.Mail.Mail.Sent, x => Assert.Null(x.UnsubscribeUrl));
    }

    [Fact]
    public async Task OptionalServiceMessagesFromStaff_LeaveOutThoseWhoTurnedThemOff()
    {
        var fixture = new UserMessageFixture(o => o.ServiceRequired = false);
        await fixture.Mail.Store.SetChoiceAsync("bo", UserMailCategories.ServiceKey, false, None);

        var preview = await fixture.Service.PreviewAsync(Everyone(UserMessageFixture.Alpha), UserMailCategories.ServiceKey, None);

        Assert.Equal(new UserMessagePreview { Recipients = 3, LeftOut = 1 }, preview);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("/terms", "/terms")]
    [InlineData(" https://example.com/terms ", "https://example.com/terms")]
    public void TheTermsOfUse_AreWhereTheHostSaid(string configured, string expected)
    {
        using var provider = Provider(o => o.TermsUrl = configured);
        using var scope = provider.CreateScope();

        Assert.Equal(expected, scope.ServiceProvider.GetRequiredService<IUserMailPreferenceService>().GetTermsUrl());
    }

    [Theory]
    [InlineData("terms")]
    [InlineData("//elsewhere.example/terms")]
    [InlineData("ftp://example.com/terms")]
    public void TermsThatAreNeitherASitePathNorAWebAddress_StopRegistration(string terms)
    {
        var e = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddThargaUserMail(o => o.TermsUrl = terms));

        Assert.Contains(nameof(UserMailOptions.TermsUrl), e.Message);
    }

    private static ServiceProvider Provider(Action<UserMailOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IHostEnvironment>());
        services.AddSingleton(Substitute.For<IUserMailPreferenceStore>());
        services.AddSingleton(Substitute.For<IUserMailOutbox>());
        services.AddSingleton(Substitute.For<IUserMailRecipientDirectory>());
        services.AddSingleton(Substitute.For<IUserService>());
        services.AddSingleton(new TeamAuthorizer(Substitute.For<ITeamPrincipalAccessor>()));
        services.AddThargaUserMail(configure);

        return services.BuildServiceProvider();
    }
}
