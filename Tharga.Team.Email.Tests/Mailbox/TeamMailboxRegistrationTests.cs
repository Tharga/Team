using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Mailbox;
using Tharga.Team.Service;

namespace Tharga.Team.Email.Tests.Mailbox;

public class TeamMailboxRegistrationTests
{
    private static ServiceProvider Build(Action<TeamMailboxOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ITeamPrincipalAccessor>());
        services.AddSingleton<ITeamMailKeyStore>(new InMemoryTeamMailKeyStore());
        services.AddSingleton<ITeamMailStore>(new InMemoryTeamMailStore());
        services.AddThargaTeamMailbox(configure);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static void Fortdocs(TeamMailboxOptions o)
    {
        o.Domain = "fortdocs.app";
        o.Suffix = "-case";
    }

    [Fact]
    public void TheMailboxService_IsRegistered_BehindTheScopeProxy()
    {
        using var provider = Build(Fortdocs);
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<ITeamMailboxService>();

        Assert.IsNotType<TeamMailboxService>(service);
    }

    [Fact]
    public void ThePostmaster_IsRegistered_BehindTheScopeProxy()
    {
        using var provider = Build(Fortdocs);
        using var scope = provider.CreateScope();

        Assert.IsNotType<PostmasterService>(scope.ServiceProvider.GetRequiredService<IPostmasterService>());
    }

    [Fact]
    public void RemovingATeam_RemovesItsMailbox()
    {
        using var provider = Build(Fortdocs);

        Assert.Contains(provider.GetServices<ITeamPurgeParticipant>(), x => x is TeamMailboxPurgeParticipant);
    }

    [Fact]
    public void TheNotifier_IsOneForTheWholeApplication()
    {
        using var provider = Build(Fortdocs);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.Same(first.ServiceProvider.GetRequiredService<ITeamMailNotifier>(), second.ServiceProvider.GetRequiredService<ITeamMailNotifier>());
    }

    [Fact]
    public void TheOptionsReachTheService()
    {
        using var provider = Build(o =>
        {
            Fortdocs(o);
            o.RetirementPeriod = TimeSpan.FromDays(14);
        });

        var options = provider.GetRequiredService<IOptions<TeamMailboxOptions>>().Value;

        Assert.Equal("fortdocs.app", options.Domain);
        Assert.Equal("-case", options.Suffix);
        Assert.Equal(TimeSpan.FromDays(14), options.RetirementPeriod);
    }

    [Fact]
    public void ByDefault_ReadingAndHandlingAreForUsers_AndChangingTheAddressForAdministrators()
    {
        using var provider = Build(Fortdocs);
        var scopes = provider.GetRequiredService<IScopeRegistry>();

        Assert.Contains(MailboxScopes.Read, scopes.GetScopesForAccessLevel(AccessLevel.User));
        Assert.Contains(MailboxScopes.Handle, scopes.GetScopesForAccessLevel(AccessLevel.User));
        Assert.DoesNotContain(MailboxScopes.Manage, scopes.GetScopesForAccessLevel(AccessLevel.User));
        Assert.Contains(MailboxScopes.Manage, scopes.GetScopesForAccessLevel(AccessLevel.Administrator));
    }

    [Fact]
    public void AScopeSetToNoLevel_IsHeldOnlyThroughARole()
    {
        using var provider = Build(o =>
        {
            Fortdocs(o);
            o.ReadAccessLevel = null;
            o.HandleAccessLevel = null;
            o.ManageAccessLevel = null;
        });
        var scopes = provider.GetRequiredService<IScopeRegistry>();

        Assert.Contains(scopes.All, x => x.Name == MailboxScopes.Manage);
        Assert.DoesNotContain(MailboxScopes.Read, scopes.GetScopesForAccessLevel(AccessLevel.User));
        Assert.DoesNotContain(MailboxScopes.Manage, scopes.GetScopesForAccessLevel(AccessLevel.Viewer));
    }

    [Fact]
    public void ThePostmasterScopes_AreSystemScopes()
    {
        using var provider = Build(Fortdocs);
        var system = provider.GetRequiredService<ISystemScopeRegistry>();

        Assert.Contains(system.All, x => x.Name == SystemMailboxScopes.PostmasterRead);
        Assert.Contains(system.All, x => x.Name == SystemMailboxScopes.PostmasterManage);
        Assert.DoesNotContain(provider.GetRequiredService<IScopeRegistry>().All, x => x.Name.StartsWith("mailbox:postmaster", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutADomain_TheMailboxRefusesToRegister()
    {
        var services = new ServiceCollection();

        var refusal = Assert.Throws<InvalidOperationException>(() => services.AddThargaTeamMailbox(_ => { }));

        Assert.Contains(nameof(TeamMailboxOptions.Domain), refusal.Message);
    }

    [Fact]
    public void AKeyTooShortToBeUnguessable_IsRefusedAtRegistration()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddThargaTeamMailbox(o =>
        {
            Fortdocs(o);
            o.KeyLength = 4;
        }));
    }

    [Fact]
    public void WithoutIMAPSettings_NothingReadsTheMailbox()
    {
        using var provider = Build(Fortdocs);

        Assert.DoesNotContain(provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>(), x => x is TeamMailboxPoller);
    }

    [Fact]
    public void WithIMAPSettings_TheMailboxIsRead()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamMailbox(o =>
        {
            Fortdocs(o);
            o.Mail.Imap.Host = "imap.example.org";
        });

        Assert.Contains(services, x => x.ImplementationType == typeof(TeamMailboxPoller));
        Assert.Contains(services, x => x.ImplementationType == typeof(PostmasterRetentionSweep));
        Assert.Contains(services, x => x.ServiceType == typeof(IMailboxSource));
    }
}
