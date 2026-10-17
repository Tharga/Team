using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Users;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>Writing to users and reading the log need the system scope and nothing else grants them: not the same name held as a team scope, and not holding nothing.</summary>
public class UserMessageScopeTests
{
    private readonly UserMessageFixture _fixture = new();
    private readonly RecordingAuditLogger _audit = new();

    private IUserMessageService Resolve(ClaimsPrincipal principal, Action<UserMessageOptions> configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddThargaUserMail(o =>
        {
            o.BaseUrl = "https://app.example.com";
            o.AddCategory(new UserMailCategory { Key = UserMailFixture.News, Name = "News" });
        });
        services.AddThargaUserMessages(configure ?? (o => o.StaffMessageCategories = [UserMailFixture.News]));

        services.AddSingleton<ITeamPrincipalAccessor>(new FixedPrincipal(principal));
        services.AddScoped(sp => new TeamAuthorizer(sp.GetRequiredService<ITeamPrincipalAccessor>()));
        services.AddSingleton(new CompositeAuditLogger([_audit], Options.Create(new AuditOptions())));
        services.AddSingleton<IAuditEntryFactory>(new AuditEntryFactory(null));
        services.AddSingleton<IUserMessageMemberDirectory>(_fixture.Directory);
        services.AddSingleton<IUserMessageLog>(_fixture.Log);
        services.AddSingleton<IUserMailOutbox>(_fixture.Mail.Outbox);
        services.AddSingleton<IUserMailPreferenceStore>(_fixture.Mail.Store);
        services.AddSingleton<IUserMailRecipientDirectory>(_fixture.Mail.Recipients);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true }).CreateScope().ServiceProvider.GetRequiredService<IUserMessageService>();
    }

    private static UserMessageRecipients Everyone => new() { AllTeams = true, EveryMember = true };

    [Fact]
    public async Task TheSystemScope_WritesAndReadsTheLog()
    {
        var service = Resolve(UserMessageFixture.Staff(new Claim(TeamClaimTypes.SystemScope, SystemMessageScopes.Send)));

        var sent = await service.SendAsync(UserMessageFixture.Message(Everyone), TestContext.Current.CancellationToken);

        Assert.Equal(5, sent.RecipientCount);
        Assert.Equal(5, (await service.PreviewAsync(Everyone, UserMailFixture.News, TestContext.Current.CancellationToken)).Recipients);
        Assert.Single((await service.GetSentAsync(cancellationToken: TestContext.Current.CancellationToken)).Items);
        Assert.NotNull(await service.GetSentMessageAsync(sent.Id, TestContext.Current.CancellationToken));
        Assert.Equal(5, (await service.GetRecipientsAsync(sent.Id, cancellationToken: TestContext.Current.CancellationToken)).Items.Length);
        Assert.NotEmpty((await service.GetChoicesAsync(TestContext.Current.CancellationToken)).Teams);
        Assert.NotEmpty(await service.SearchUsersAsync(new UserMessageUserSearch { Text = "anna", AllTeams = true }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheSameNameAsATeamScope_GrantsNothing()
    {
        var service = Resolve(UserMessageFixture.Staff(
            new Claim(TeamClaimTypes.TeamKey, UserMessageFixture.Alpha),
            new Claim(TeamClaimTypes.Scope, SystemMessageScopes.Send)));

        await AssertRefusedEverywhereAsync(service);
    }

    [Fact]
    public async Task NoScope_GrantsNothing_AndTheRefusalIsAudited()
    {
        var service = Resolve(UserMessageFixture.Staff());

        await AssertRefusedEverywhereAsync(service);

        Assert.Empty(_fixture.Mail.Outbox.Items);
        Assert.Contains(_audit.Entries, x => x.EventType == AuditEventType.ScopeDenial && x.MethodName == nameof(IUserMessageService.SendAsync));
    }

    [Fact]
    public async Task AnAllowedCall_LeavesOnlyTheServicesOwnEntry()
    {
        var service = Resolve(UserMessageFixture.Staff(new Claim(TeamClaimTypes.SystemScope, SystemMessageScopes.Send)));

        await service.PreviewAsync(Everyone, UserMailFixture.News, TestContext.Current.CancellationToken);
        await service.SendAsync(UserMessageFixture.Message(Everyone), TestContext.Current.CancellationToken);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(UserMessageAudit.SendAction, entry.Action);
    }

    [Fact]
    public void TheScope_IsRegisteredAsASystemScope_AndNoTeamScopeIsAdded()
    {
        var services = new ServiceCollection();
        services.AddThargaUserMail();
        services.AddThargaUserMessages();

        var provider = services.BuildServiceProvider();

        Assert.Contains(provider.GetRequiredService<ISystemScopeRegistry>().All, x => x.Name == SystemMessageScopes.Send);
        Assert.DoesNotContain(provider.GetService<IScopeRegistry>()?.All ?? [], x => x.Name == SystemMessageScopes.Send);
    }

    [Fact]
    public void ByDefault_StaffWriteServiceMessages()
    {
        Assert.Equal([UserMailCategories.ServiceKey], new UserMessageOptions().StaffMessageCategories);
    }

    [Fact]
    public void MessagesNeedMailToUsersRegisteredFirst()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.AddThargaUserMessages());
    }

    [Fact]
    public void ACategoryNobodyRegistered_IsRefusedAtRegistration()
    {
        var services = new ServiceCollection();
        services.AddThargaUserMail();

        var e = Assert.Throws<InvalidOperationException>(() => services.AddThargaUserMessages(o => o.StaffMessageCategories = ["news"]));
        Assert.Contains("'news'", e.Message);
    }

    [Fact]
    public void NoCategoryAtAll_IsRefusedAtRegistration()
    {
        var services = new ServiceCollection();
        services.AddThargaUserMail();

        Assert.Throws<InvalidOperationException>(() => services.AddThargaUserMessages(o => o.StaffMessageCategories = []));
    }

    private static async Task AssertRefusedEverywhereAsync(IUserMessageService service)
    {
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetChoicesAsync(ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PreviewAsync(Everyone, UserMailFixture.News, ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SendAsync(UserMessageFixture.Message(Everyone), ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetSentAsync(cancellationToken: ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetSentMessageAsync("any", ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetRecipientsAsync("any", cancellationToken: ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SearchUsersAsync(new UserMessageUserSearch { Text = "anna", AllTeams = true }, ct));
    }

    private sealed class FixedPrincipal(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
