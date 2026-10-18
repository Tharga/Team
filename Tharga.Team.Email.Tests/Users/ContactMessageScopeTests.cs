using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Users;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>Writing to contacts, reading the log and opting an address out need the system scope and nothing else grants them: not the scope for messages to users, not the same name held as a team scope, and not holding nothing.</summary>
public class ContactMessageScopeTests
{
    private readonly ContactMessageFixture _fixture = new();
    private readonly RecordingAuditLogger _audit = new();

    private IContactMessageService Resolve(ClaimsPrincipal principal)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddThargaUserMail(o =>
        {
            o.BaseUrl = "https://app.example.com";
            o.Mail.FromAddress = "hello@example.com";
            o.AddCategory(new UserMailCategory { Key = UserMailFixture.News, Name = "News" });
        });
        services.AddThargaContactMessages(o => o.AddCategory(new ContactMailCategory { Key = ContactMailFixture.Offers, Name = "Offers" }));

        services.AddSingleton<ITeamPrincipalAccessor>(new FixedPrincipal(principal));
        services.AddScoped(sp => new TeamAuthorizer(sp.GetRequiredService<ITeamPrincipalAccessor>()));
        services.AddSingleton(new CompositeAuditLogger([_audit], Options.Create(new AuditOptions())));
        services.AddSingleton<IAuditEntryFactory>(new AuditEntryFactory(null));
        services.AddSingleton<IContactMessageLog>(_fixture.Log);
        services.AddSingleton<IContactMailSuppressionStore>(_fixture.Contacts.Suppressions);
        services.AddSingleton<IUserMailOutbox>(_fixture.Mail.Outbox);
        services.AddSingleton<IUserMailPreferenceStore>(_fixture.Mail.Store);
        services.AddSingleton<IUserMailRecipientDirectory>(_fixture.Mail.Recipients);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true }).CreateScope().ServiceProvider.GetRequiredService<IContactMessageService>();
    }

    private static ContactAddressee[] List => [ContactMessageFixture.Row("kim@customer.example", "c-1"), ContactMessageFixture.Row("lee@customer.example", "c-2")];

    [Fact]
    public async Task TheSystemScope_WritesReadsAndOptsOut()
    {
        var service = Resolve(UserMessageFixture.Staff(new Claim(TeamClaimTypes.SystemScope, SystemContactScopes.Send)));
        var ct = TestContext.Current.CancellationToken;

        var sent = await service.SendAsync(ContactMessageFixture.Message(List), ct);

        Assert.Equal(2, sent.RecipientCount);
        Assert.Equal(["c-1", "c-2"], sent.Recipients.SelectMany(x => x.References));
        Assert.NotEmpty((await service.GetChoicesAsync(ct)).Categories);
        Assert.Equal(2, (await service.GetAddresseesAsync(List, ContactMailFixture.Offers, ct)).Reached);
        Assert.Contains("Unsubscribe", (await service.PreviewMailAsync(ContactMessageFixture.Message(List), ct)).Html);
        Assert.Single((await service.GetSentAsync(cancellationToken: ct)).Items);
        Assert.NotNull(await service.GetSentMessageAsync(sent.Id, ct));
        Assert.Equal(2, (await service.GetRecipientsAsync(sent.Id, cancellationToken: ct)).Items.Length);
        Assert.True(await service.SuppressAsync("kim@customer.example", cancellationToken: ct));
    }

    [Fact]
    public async Task TheScopeForMessagesToUsers_GrantsNothingHere()
    {
        var service = Resolve(UserMessageFixture.Staff(new Claim(TeamClaimTypes.SystemScope, SystemMessageScopes.Send)));

        await AssertRefusedEverywhereAsync(service);
    }

    [Fact]
    public async Task TheSameNameAsATeamScope_GrantsNothing()
    {
        var service = Resolve(UserMessageFixture.Staff(
            new Claim(TeamClaimTypes.TeamKey, UserMessageFixture.Alpha),
            new Claim(TeamClaimTypes.Scope, SystemContactScopes.Send)));

        await AssertRefusedEverywhereAsync(service);
    }

    [Fact]
    public async Task NoScope_GrantsNothing_AndTheRefusalIsAudited()
    {
        var service = Resolve(UserMessageFixture.Staff());

        await AssertRefusedEverywhereAsync(service);

        Assert.Empty(_fixture.Mail.Outbox.Items);
        Assert.Empty(_fixture.Contacts.Suppressions.Items);
        Assert.Contains(_audit.Entries, x => x.EventType == AuditEventType.ScopeDenial && x.MethodName == nameof(IContactMessageService.SendAsync));
        Assert.Contains(_audit.Entries, x => x.EventType == AuditEventType.ScopeDenial && x.MethodName == nameof(IContactMessageService.SuppressAsync));
    }

    [Fact]
    public async Task AnAllowedCall_LeavesOnlyTheServicesOwnEntry()
    {
        var service = Resolve(UserMessageFixture.Staff(new Claim(TeamClaimTypes.SystemScope, SystemContactScopes.Send)));
        var ct = TestContext.Current.CancellationToken;

        await service.GetAddresseesAsync(List, ContactMailFixture.Offers, ct);
        await service.PreviewMailAsync(ContactMessageFixture.Message(List), ct);
        await service.SendAsync(ContactMessageFixture.Message(List), ct);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal((ContactMessageAudit.Feature, ContactMessageAudit.SendAction), (entry.Feature, entry.Action));
    }

    [Fact]
    public void TheScope_IsRegisteredAsASystemScope_AndNoTeamScopeIsAdded()
    {
        var services = new ServiceCollection();
        services.AddThargaUserMail(o => o.BaseUrl = "https://app.example.com");
        services.AddThargaContactMessages(o => o.AddCategory(new ContactMailCategory { Key = ContactMailFixture.Offers, Name = "Offers" }));

        var provider = services.BuildServiceProvider();

        Assert.Contains(provider.GetRequiredService<ISystemScopeRegistry>().All, x => x.Name == SystemContactScopes.Send);
        Assert.DoesNotContain(provider.GetService<IScopeRegistry>()?.All ?? [], x => x.Name == SystemContactScopes.Send);
    }

    [Fact]
    public void AServiceOfTheHostsOwn_IsKept()
    {
        var services = new ServiceCollection();
        var mine = Substitute.For<IContactMessageService>();
        services.AddSingleton(mine);
        services.AddThargaUserMail(o => o.BaseUrl = "https://app.example.com");
        services.AddThargaContactMessages(o => o.AddCategory(new ContactMailCategory { Key = ContactMailFixture.Offers, Name = "Offers" }));

        Assert.Same(mine, Assert.Single(services, x => x.ServiceType == typeof(IContactMessageService)).ImplementationInstance);
    }

    private static async Task AssertRefusedEverywhereAsync(IContactMessageService service)
    {
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetChoicesAsync(ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAddresseesAsync(List, ContactMailFixture.Offers, ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PreviewMailAsync(ContactMessageFixture.Message(List), ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SendAsync(ContactMessageFixture.Message(List), ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetSentAsync(cancellationToken: ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetSentMessageAsync("any", ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetRecipientsAsync("any", cancellationToken: ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SuppressAsync("kim@customer.example", cancellationToken: ct));
    }

    private sealed class FixedPrincipal(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
