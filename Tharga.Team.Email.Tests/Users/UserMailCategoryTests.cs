using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tharga.Team.Email.Users;
using Tharga.Team.Service;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>Categories are the host's to register, beside two built-in required ones, and a mistake in them stops the host at startup.</summary>
public class UserMailCategoryTests
{
    private static UserMailCategory Category(string key, bool required = false) => new() { Key = key, Name = "Name of " + key, Required = required };

    [Fact]
    public void TheBuiltInCategories_ComeFirst_AndAreRequired_ThenTheHostsInOrder()
    {
        var registry = UserMailCategoryRegistry.Create([Category("news"), Category("billing", required: true)]);

        Assert.Equal(["account", "service", "news", "billing"], registry.All.Select(x => x.Key));
        Assert.All(registry.All.Take(2), x => Assert.True(x.Required));
        Assert.True(registry.HasOptional);
    }

    [Fact]
    public void WithOnlyRequiredCategories_NothingNeedsAnUnsubscribeLink()
    {
        Assert.False(UserMailCategoryRegistry.Create([Category("billing", required: true)]).HasOptional);
    }

    [Theory]
    [InlineData("")]
    [InlineData("News")]
    [InlineData("has space")]
    [InlineData("-leading")]
    [InlineData("åäö")]
    public void AMalformedKey_IsRefused(string key)
    {
        var e = Assert.Throws<InvalidOperationException>(() => UserMailCategoryRegistry.Create([Category(key)]));
        Assert.Contains("not valid", e.Message);
    }

    [Theory]
    [InlineData(UserMailCategories.AccountKey)]
    [InlineData(UserMailCategories.ServiceKey)]
    public void ABuiltInKey_IsReserved(string key)
    {
        var e = Assert.Throws<InvalidOperationException>(() => UserMailCategoryRegistry.Create([Category(key)]));
        Assert.Contains("built in", e.Message);
    }

    [Fact]
    public void AKeyRegisteredTwice_IsRefused()
    {
        var e = Assert.Throws<InvalidOperationException>(() => UserMailCategoryRegistry.Create([Category("news"), Category("news")]));
        Assert.Contains("twice", e.Message);
    }

    [Fact]
    public void ACategoryWithoutAName_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => UserMailCategoryRegistry.Create([new UserMailCategory { Key = "news", Name = " " }]));
    }

    [Fact]
    public void AnUnknownKey_IsNotFound_AndAskingForItNamesIt()
    {
        var registry = UserMailCategoryRegistry.Create([Category("news")]);

        Assert.Null(registry.Find("nope"));
        Assert.Contains("'nope'", Assert.Throws<ArgumentException>(() => registry.Get("nope", "category")).Message);
    }

    [Fact]
    public void TextKeys_DefaultFromTheCategoryKey_UnlessGiven()
    {
        var category = new UserMailCategory { Key = "news", Name = "News", Description = "About news." };

        Assert.Equal("userMail.category.news.name", category.NameKey);
        Assert.Equal("userMail.category.news.description", category.DescriptionKey);
        Assert.Equal("app.news", (category with { NameKey = "app.news" }).NameKey);
    }

    [Theory]
    [InlineData(false, true, null, true)]
    [InlineData(false, false, null, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, true)]
    [InlineData(true, true, false, true)]
    public void Receives_IsTheChoiceOrTheDefault_AndAlwaysForARequiredCategory(bool required, bool onByDefault, bool? choice, bool expected)
    {
        var category = new UserMailCategory { Key = "news", Name = "News", Required = required, OnByDefault = onByDefault };

        Assert.Equal(expected, category.Receives(choice));
    }

    [Fact]
    public void AnOptionalCategory_WithoutABaseUrl_StopsRegistration()
    {
        var e = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddThargaUserMail(o => o.AddCategory(Category("news"))));
        Assert.Contains(nameof(UserMailOptions.BaseUrl), e.Message);
    }

    [Fact]
    public void OnlyRequiredCategories_NeedNoBaseUrl()
    {
        new ServiceCollection().AddThargaUserMail(o => o.AddCategory(Category("billing", required: true)));
    }

    [Fact]
    public void ABaseUrlThatIsNotAnAbsoluteWebAddress_StopsRegistration()
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddThargaUserMail(o => o.BaseUrl = "app.example.com"));
    }

    [Fact]
    public void AnAttachmentLimitOverTheCeiling_StopsRegistration()
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddThargaUserMail(o => o.MaxAttachmentBytes = UserMailOptions.AttachmentBytesCeiling + 1));
    }

    [Fact]
    public void ACategoryMistake_StopsRegistration()
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddThargaUserMail(o =>
        {
            o.BaseUrl = "https://app.example.com";
            o.AddCategory(Category(UserMailCategories.AccountKey));
        }));
    }

    [Fact]
    public void TheRegistration_ResolvesEverythingAHostUses_OnceTheStoresAndThePlatformAreThere()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IHostEnvironment>());
        services.AddSingleton(Substitute.For<IUserMailPreferenceStore>());
        services.AddSingleton(Substitute.For<IUserMailOutbox>());
        services.AddSingleton(Substitute.For<IUserMailRecipientDirectory>());
        services.AddSingleton(Substitute.For<IUserService>());
        services.AddSingleton(new TeamAuthorizer(Substitute.For<ITeamPrincipalAccessor>()));
        services.AddThargaUserMail(o =>
        {
            o.BaseUrl = "https://app.example.com";
            o.AddCategory(Category("news"));
        });

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUserMailQueue>());
        Assert.Equal(["account", "service", "news"], scope.ServiceProvider.GetRequiredService<IUserMailPreferenceService>().GetCategories().Select(x => x.Key));
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<UserMailDispatcher>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<UserMailUnsubscribe>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());
        Assert.Contains(provider.GetServices<IHostedService>(), x => x is UserMailSender);
    }
}
