using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tharga.Team.Email.Users;
using Tharga.Team.Service;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>The catalogue is checked when it is registered: a notice in an unknown category, with no delivery or a template naming an undeclared value never reaches a user.</summary>
public class UserNoticeCatalogueTests
{
    private readonly UserMailFixture _mail = new(o => o.AddCategory(new UserMailCategory { Key = UserNoticeFixture.Work, Name = "Work" }));

    private UserNoticeRegistry Create(UserNoticeType notice) => UserNoticeRegistry.Create([notice], _mail.Categories);

    private static string Refusal(Func<UserNoticeRegistry> create) => Assert.Throws<InvalidOperationException>(create).Message;

    [Fact]
    public void AValidCatalogue_IsKept_InTheOrderItWasAdded()
    {
        var registry = UserNoticeRegistry.Create([UserNoticeFixture.Opened, UserNoticeFixture.Assigned], _mail.Categories);

        Assert.Equal(["case.opened", "case.assigned"], registry.All.Select(x => x.Key));
        Assert.Equal("userNotice.case.opened.name", registry.Find("case.opened").NameKey);
        Assert.Equal("userNotice.case.opened.subject", registry.Find("case.opened").SubjectKey);
    }

    [Fact]
    public void ANoticeInAnUnknownCategory_IsRefused()
    {
        var message = Refusal(() => Create(UserNoticeFixture.Assigned with { Category = "nowhere" }));

        Assert.Contains("'nowhere'", message);
    }

    [Fact]
    public void ANoticeWithNoDelivery_IsRefused()
    {
        Refusal(() => Create(UserNoticeFixture.Assigned with { Deliveries = [] }));
        Refusal(() => Create(UserNoticeFixture.Assigned with { Deliveries = null }));
    }

    [Fact]
    public void ADefaultDeliveryTheNoticeDoesNotAllow_IsRefused()
    {
        Refusal(() => Create(UserNoticeFixture.Joined with { DefaultDelivery = UserNoticeDelivery.Digest }));
    }

    [Theory]
    [InlineData("Hello {who}", null)]
    [InlineData("Hello", "<p>{who}</p>")]
    public void ATemplateUsingAnUndeclaredPlaceholder_IsRefused(string subject, string body)
    {
        var message = Refusal(() => Create(UserNoticeFixture.Joined with { Subject = subject, Body = body }));

        Assert.Contains("{who}", message);
    }

    [Theory]
    [InlineData("Hello {actor")]
    [InlineData("Hello actor}")]
    [InlineData("Hello {1actor}")]
    [InlineData("Hello { }")]
    public void AMalformedTemplate_IsRefused(string subject)
    {
        Refusal(() => Create(UserNoticeFixture.Assigned with { Subject = subject }));
    }

    [Fact]
    public void BracesWrittenTwice_AreText_AndTheBuiltInSubjectNeedsNoDeclaration()
    {
        var registry = Create(UserNoticeFixture.Joined with { Subject = "{{draft}} {subject}" });

        Assert.Equal("{draft} Case 1", UserNoticeTemplate.Render(registry.All[0].Subject, new Dictionary<string, string> { ["subject"] = "Case 1" }, x => x));
    }

    [Theory]
    [InlineData("1st")]
    [InlineData("with space")]
    [InlineData("subject")]
    public void ABadPlaceholderDeclaration_IsRefused(string name)
    {
        Refusal(() => Create(UserNoticeFixture.Joined with { Placeholders = [name] }));
    }

    [Fact]
    public void APlaceholderDeclaredTwice_IsRefused()
    {
        Refusal(() => Create(UserNoticeFixture.Assigned with { Placeholders = ["actor", "actor"] }));
    }

    [Theory]
    [InlineData("Case.Assigned")]
    [InlineData("case assigned")]
    [InlineData("")]
    public void AMalformedKey_IsRefused(string key)
    {
        Refusal(() => Create(UserNoticeFixture.Joined with { Key = key }));
    }

    [Fact]
    public void ANoticeWithoutNameSubjectOrAudience_IsRefused()
    {
        Refusal(() => Create(UserNoticeFixture.Joined with { Name = " " }));
        Refusal(() => Create(UserNoticeFixture.Joined with { Subject = "" }));
        Refusal(() => Create(UserNoticeFixture.Joined with { Audience = null }));
    }

    [Fact]
    public void TheSameKeyTwice_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => UserNoticeRegistry.Create([UserNoticeFixture.Joined, UserNoticeFixture.Joined], _mail.Categories));
    }

    [Fact]
    public void AnAudienceWithoutWhatItNames_IsRefusedWhereItIsWritten()
    {
        Assert.Throws<ArgumentException>(() => UserNoticeAudience.WithScope(" "));
        Assert.ThrowsAny<ArgumentException>(() => UserNoticeAudience.WithRole(null));
        Assert.Throws<ArgumentNullException>(() => UserNoticeAudience.ResolvedBy(null));
    }

    [Fact]
    public void ValuesAreEncodedInTheBody_AndTakenAsTheyAreInTheSubject()
    {
        var values = new Dictionary<string, string> { ["actor"] = "<Bo & Co>" };

        Assert.Equal("<p>&lt;Bo &amp; Co&gt;</p>", UserNoticeTemplate.Render("<p>{actor}</p>", values, System.Net.WebUtility.HtmlEncode));
        Assert.Equal("By <Bo & Co>", UserNoticeTemplate.Render("By {actor}", values, x => x));
    }

    [Fact]
    public void Notices_NeedMailToUsersRegisteredFirst()
    {
        var e = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddThargaUserNotices());

        Assert.Contains(nameof(UserMailRegistration.AddThargaUserMail), e.Message);
    }

    [Fact]
    public void Notices_NeedTheSitesAddress_ForTheirLinks()
    {
        var services = new ServiceCollection();
        services.AddThargaUserMail(o => o.AddCategory(new UserMailCategory { Key = UserNoticeFixture.Work, Name = "Work", Required = true }));

        var e = Assert.Throws<InvalidOperationException>(() => services.AddThargaUserNotices(o => o.Add(UserNoticeFixture.Joined)));
        Assert.Contains(nameof(UserMailOptions.BaseUrl), e.Message);
    }

    [Fact]
    public void ACategoryNotRegistered_IsRefusedAtRegistration()
    {
        var services = new ServiceCollection();
        services.AddThargaUserMail(o => o.BaseUrl = "https://app.example.com");

        Assert.Throws<InvalidOperationException>(() => services.AddThargaUserNotices(o => o.Add(UserNoticeFixture.Joined)));
    }

    [Fact]
    public void AnUnknownTimeZone_IsRefusedAtRegistration()
    {
        var services = Mail();

        var e = Assert.Throws<InvalidOperationException>(() => services.AddThargaUserNotices(o => o.TimeZoneId = "Nowhere/Never"));
        Assert.Contains("Nowhere/Never", e.Message);
    }

    [Fact]
    public void ANegativeBurstWindow_IsRefusedAtRegistration()
    {
        Assert.Throws<InvalidOperationException>(() => Mail().AddThargaUserNotices(o => o.BurstWindow = TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Registration_ResolvesEverything_WithTheStoresInPlace()
    {
        var fixture = new UserNoticeFixture();
        var services = Mail();
        services.AddLogging();
        services.AddThargaUserNotices(o => o.Add(UserNoticeFixture.Assigned));
        services.AddSingleton<ITeamPrincipalAccessor>(Substitute.For<ITeamPrincipalAccessor>());
        services.AddScoped(sp => new TeamAuthorizer(sp.GetRequiredService<ITeamPrincipalAccessor>()));
        services.AddSingleton(Substitute.For<IUserService>());
        services.AddSingleton<IUserNoticeOutbox>(fixture.Outbox);
        services.AddSingleton<IUserNoticePreferenceStore>(fixture.Store);
        services.AddSingleton<IUserMessageMemberDirectory>(fixture.Directory);
        services.AddSingleton<IUserMailOutbox>(fixture.Mail.Outbox);
        services.AddSingleton<IUserMailPreferenceStore>(fixture.Mail.Store);
        services.AddSingleton<IUserMailRecipientDirectory>(fixture.Mail.Recipients);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUserNoticeRaiser>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUserNoticePreferenceService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUserNoticeTeamService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<UserNoticeDistributor>());
        Assert.Contains(provider.GetServices<IHostedService>(), x => x is UserNoticeSender);
        Assert.Contains(scope.ServiceProvider.GetServices<ITeamPurgeParticipant>(), x => x is UserNoticePurgeParticipant);
    }

    private static ServiceCollection Mail()
    {
        var services = new ServiceCollection();
        services.AddThargaUserMail(o =>
        {
            o.BaseUrl = "https://app.example.com";
            o.AddCategory(new UserMailCategory { Key = UserNoticeFixture.Work, Name = "Work" });
        });
        return services;
    }
}
