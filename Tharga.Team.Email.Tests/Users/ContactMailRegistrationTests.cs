using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Email.Users;
using Tharga.Team.Service;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>
/// Contact categories are registered beside the categories of mail to users and checked when they are: after mail to users,
/// with the site address unsubscribe links need, at least one category, every key well formed, unique and not a user
/// category's, every sender address an address; and they are never offered to users as a choice.
/// </summary>
public class ContactMailRegistrationTests
{
    private static ServiceCollection WithUserMail(Action<UserMailOptions> configure = null)
    {
        var services = new ServiceCollection();
        services.AddThargaUserMail(o =>
        {
            o.BaseUrl = "https://app.example.com";
            o.AddCategory(new UserMailCategory { Key = "news", Name = "News" });
            configure?.Invoke(o);
        });
        return services;
    }

    private static void Offers(ContactMessageOptions o) => o.AddCategory(new ContactMailCategory { Key = "offers", Name = "Offers" });

    [Fact]
    public void ContactMail_NeedsMailToUsersRegisteredFirst()
    {
        var e = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddThargaContactMessages(Offers));

        Assert.Contains(nameof(UserMailRegistration.AddThargaUserMail), e.Message);
    }

    [Fact]
    public void ContactMail_NeedsTheSiteAddress_ForItsUnsubscribeLinks()
    {
        var services = new ServiceCollection();
        services.AddThargaUserMail();

        var e = Assert.Throws<InvalidOperationException>(() => services.AddThargaContactMessages(Offers));

        Assert.Contains(nameof(UserMailOptions.BaseUrl), e.Message);
    }

    [Fact]
    public void NoCategory_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => WithUserMail().AddThargaContactMessages());
    }

    [Theory]
    [InlineData("news")]
    [InlineData(UserMailCategories.ServiceKey)]
    [InlineData(UserMailCategories.AccountKey)]
    public void AKeyACategoryOfMailToUsersHas_IsRefused(string key)
    {
        var e = Assert.Throws<InvalidOperationException>(() => WithUserMail().AddThargaContactMessages(o => o.AddCategory(new ContactMailCategory { Key = key, Name = "Mine" })));

        Assert.Contains($"'{key}'", e.Message);
    }

    [Theory]
    [InlineData("Offers")]
    [InlineData("*")]
    [InlineData("")]
    [InlineData("-offers")]
    public void AMalformedKey_IsRefused(string key)
    {
        Assert.Throws<InvalidOperationException>(() => WithUserMail().AddThargaContactMessages(o => o.AddCategory(new ContactMailCategory { Key = key, Name = "Offers" })));
    }

    [Fact]
    public void ARepeatedKey_OrAMissingName_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => WithUserMail().AddThargaContactMessages(o =>
        {
            Offers(o);
            Offers(o);
        }));
        Assert.Throws<InvalidOperationException>(() => WithUserMail().AddThargaContactMessages(o => o.AddCategory(new ContactMailCategory { Key = "offers", Name = " " })));
    }

    [Theory]
    [InlineData(nameof(ContactMessageOptions.FromAddress))]
    [InlineData(nameof(ContactMessageOptions.ReplyTo))]
    public void ASenderAddressThatIsNotOne_IsRefused(string option)
    {
        var e = Assert.Throws<InvalidOperationException>(() => WithUserMail().AddThargaContactMessages(o =>
        {
            Offers(o);
            typeof(ContactMessageOptions).GetProperty(option)!.SetValue(o, "not an address");
        }));

        Assert.Contains(option, e.Message);
    }

    [Fact]
    public void ACategorysTextKeys_DefaultToTheirOwnNamespace()
    {
        var category = new ContactMailCategory { Key = "offers", Name = "Offers" };

        Assert.Equal(("contactMail.category.offers.name", "contactMail.category.offers.description"), (category.NameKey, category.DescriptionKey));
        Assert.Equal("mine", (category with { NameKey = "mine" }).NameKey);
    }

    [Fact]
    public void Registered_TheSendingPiecesResolve_AndContactCategoriesAreNeverAUsersChoice()
    {
        var services = WithUserMail();
        services.AddLogging();
        services.AddThargaContactMessages(Offers);
        services.AddSingleton(Substitute.For<IContactMailSuppressionStore>());
        services.AddSingleton(Substitute.For<IUserMailOutbox>());
        services.AddSingleton(Substitute.For<IUserMailPreferenceStore>());
        services.AddSingleton(Substitute.For<IUserMailRecipientDirectory>());
        services.AddSingleton(Substitute.For<IUserService>());
        services.AddSingleton(new TeamAuthorizer(Substitute.For<ITeamPrincipalAccessor>()));

        using var scope = services.BuildServiceProvider().CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<UserMailDispatcher>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ContactMailSending>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ContactMailUnsubscribe>());
        Assert.DoesNotContain(scope.ServiceProvider.GetRequiredService<IUserMailPreferenceService>().GetCategories(), x => x.Key == "offers");
    }

    [Fact]
    public void TheUserMailQueue_RefusesContactMail_WhichOnlyTheContactMessageServiceQueues()
    {
        var fixture = new UserMailFixture();

        Assert.Throws<ArgumentException>(() => fixture.Queue.Accept(new UserMail { RecipientUserKey = UserMailFixture.UserKey, Category = UserMailFixture.News, Subject = "S", Body = "B", Kind = UserMailKind.Contact }));
        Assert.Throws<ArgumentException>(() => fixture.Queue.Accept(new UserMailBatch { RecipientUserKeys = [UserMailFixture.UserKey], Category = UserMailFixture.News, Subject = "S", Body = "B", Kind = UserMailKind.Contact }));
        Assert.Throws<ArgumentException>(() => fixture.Queue.Accept(new UserMailBatch { RecipientUserKeys = [UserMailFixture.UserKey], RecipientAddresses = [new UserMailBatchAddress("kim@customer.example")], Category = UserMailFixture.News, Subject = "S", Body = "B" }));
    }
}
