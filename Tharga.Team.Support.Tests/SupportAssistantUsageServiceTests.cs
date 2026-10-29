using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Reading the assistant's usage: the store's totals for a period, for one team or all, only to a caller holding the
/// system scope; and a purged team's records go with it.
/// </summary>
public class SupportAssistantUsageServiceTests
{
    private static readonly DateTime From = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly SupportAssistantUsageTotal[] Totals =
    [
        new() { TeamKey = "team-a", Provider = "openai", Model = "gpt-4.1", Answers = 3, TotalTokens = 900 },
        new() { TeamKey = "team-b", Provider = "ollama", Model = "llama3", Answers = 1, Declines = 1, TotalTokens = 200 }
    ];

    private readonly ISupportAssistantUsageStore _store = Substitute.For<ISupportAssistantUsageStore>();

    [Fact]
    public async Task TheTotals_AreTheStores_ForThePeriod_AndAllTeams()
    {
        _store.GetTotalsAsync(From, To, null, Arg.Any<CancellationToken>()).Returns(Totals);

        var usage = await Service(withScope: true).GetUsageAsync(From, To);

        Assert.Equal(Totals, usage);
    }

    [Fact]
    public async Task OneTeam_IsAskedOfTheStore()
    {
        _store.GetTotalsAsync(From, To, "team-a", Arg.Any<CancellationToken>()).Returns([Totals[0]]);

        var usage = await Service(withScope: true).GetUsageAsync(From, To, "team-a");

        Assert.Equal("team-a", Assert.Single(usage).TeamKey);
    }

    /// <summary>Holding a team scope, even administering the team, is not this grant.</summary>
    [Fact]
    public async Task ACallerWithoutTheSystemScope_IsRefused_AndTheStoreIsNotRead()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(withScope: false).GetUsageAsync(From, To, "team-a"));

        await _store.DidNotReceiveWithAnyArgs().GetTotalsAsync(default, default);
    }

    [Fact]
    public async Task WithNoStore_ThereIsNoUsage()
    {
        var service = new AuthorizationSupportAssistantUsageServiceDecorator(new SupportAssistantUsageService(), Authorizer(withScope: true));

        Assert.Empty(await service.GetUsageAsync(From, To));
    }

    [Fact]
    public async Task PurgingATeam_RemovesItsUsage()
    {
        _store.DeleteForTeamAsync("team-a", Arg.Any<CancellationToken>()).Returns(12);

        Assert.Equal(12, await new SupportAssistantUsagePurgeParticipant(_store).PurgeTeamDataAsync("team-a"));
    }

    [Fact]
    public async Task Registered_TheServiceReadsTheStore_TheScopeIsASystemScope_AndPurgingIncludesUsage()
    {
        _store.GetTotalsAsync(From, To, null, Arg.Any<CancellationToken>()).Returns(Totals);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ISupportCaseStore>());
        services.AddSingleton(_store);
        services.AddSingleton(Accessor(withScope: true));
        services.AddSingleton<TeamAuthorizer>();
        services.Configure<AuditOptions>(_ => { });
        services.AddSingleton<CompositeAuditLogger>();
        services.AddSingleton<IAuditEntryFactory>(new AuditEntryFactory(null));
        services.AddThargaSupportCases();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.Equal(Totals, await scope.ServiceProvider.GetRequiredService<ISupportAssistantUsageService>().GetUsageAsync(From, To));
        Assert.Contains(scope.ServiceProvider.GetServices<ITeamPurgeParticipant>(), x => x is SupportAssistantUsagePurgeParticipant);
        Assert.Contains(SystemSupportScopes.UsageRead, scope.ServiceProvider.GetRequiredService<ISystemScopeRegistry>().All.Select(x => x.Name));
    }

    /// <summary>Without a store there is nothing to purge, and no participant claims there is.</summary>
    [Fact]
    public void RegisteredWithNoStore_NoUsageIsPurged()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ISupportCaseStore>());
        services.AddThargaSupportCases();

        using var provider = services.BuildServiceProvider();
        Assert.DoesNotContain(provider.GetServices<ITeamPurgeParticipant>(), x => x is SupportAssistantUsagePurgeParticipant);
    }

    private ISupportAssistantUsageService Service(bool withScope)
        => new AuthorizationSupportAssistantUsageServiceDecorator(new SupportAssistantUsageService(_store), Authorizer(withScope));

    private static TeamAuthorizer Authorizer(bool withScope) => new(Accessor(withScope));

    private static ITeamPrincipalAccessor Accessor(bool withScope)
    {
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, "staff"), new(TeamClaimTypes.TeamKey, "team-a"), new(TeamClaimTypes.Scope, SupportScopes.AssistantManage)];
        if (withScope) claims.Add(new Claim(TeamClaimTypes.SystemScope, SystemSupportScopes.UsageRead));
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(ValueTask.FromResult(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));
        return accessor;
    }
}
