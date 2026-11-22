using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// What a team may spend on the assistant: the default limit and a team's own, what is shown instead of money, how many questions are left on each
/// model, and who may read the amounts and who may change a limit.
/// </summary>
public class SupportAssistantAllowanceTests
{
    private const string Team = "team-a";

    private static readonly DateTime Now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodStart = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly PlatformAiModel Sonnet = new() { Id = "sonnet", ModelId = "claude-sonnet", DisplayName = "Sonnet", Offered = true, PriceInput = 2m, PriceOutput = 10m, Currency = "USD" };
    private static readonly PlatformAiModel Large = new() { Id = "large", ModelId = "mistral-large", DisplayName = "Large", Offered = true, PriceInput = 0.7m, PriceOutput = 2.1m, Currency = "USD" };

    private readonly ISupportAssistantUsageStore _usage = Substitute.For<ISupportAssistantUsageStore>();
    private readonly ISupportAssistantLimitStore _limits = Substitute.For<ISupportAssistantLimitStore>();
    private readonly ISupportAssistantPricing _pricing = Substitute.For<ISupportAssistantPricing>();
    private readonly IPlatformAiCatalogueStore _catalogue = Substitute.For<IPlatformAiCatalogueStore>();
    private readonly SupportAssistantAllowanceOptions _options = new() { DefaultLimit = 10m };

    public SupportAssistantAllowanceTests()
    {
        _pricing.GetAsync(Team, Arg.Any<CancellationToken>()).Returns(Sonnet);
        _catalogue.GetAsync(Arg.Any<CancellationToken>()).Returns(new StoredPlatformAiCatalogue { Models = [Sonnet, Large], DefaultModelId = "sonnet" });
        Spent(4m, answers: 40, tokens: 40_000);
    }

    private DefaultSupportAssistantAllowanceSource Source() => new(_options, new FixedClock(Now), _usage, _limits, _pricing, _catalogue);

    private void Spent(decimal cost, long answers, long tokens, string model = "claude-sonnet")
        => _usage.GetTotalsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Team, Arg.Any<CancellationToken>())
            .Returns([new SupportAssistantUsageTotal { TeamKey = Team, Model = model, Answers = answers, Cost = cost, TotalTokens = tokens, Currency = "USD" }]);

    [Fact]
    public async Task WithNoLimit_NoTeamIsLimited()
    {
        _options.DefaultLimit = null;

        var allowance = await Source().GetAsync(Team);

        Assert.False(allowance.Applies);
        Assert.Null(allowance.Share);
    }

    /// <summary>A team on its own provider costs the platform nothing, so it has no bar and no limit.</summary>
    [Fact]
    public async Task ATeamNotAnsweredByThePlatformsModel_IsNeverLimited()
    {
        _pricing.GetAsync(Team, Arg.Any<CancellationToken>()).Returns((PlatformAiModel)null);

        Assert.False((await Source().GetAsync(Team)).Applies);
    }

    [Fact]
    public async Task TheDefaultLimit_HoldsAgainstThisMonthsCost()
    {
        var allowance = await Source().GetAsync(Team);

        Assert.True(allowance.Applies);
        Assert.Equal(0.4m, allowance.Share);
        Assert.False(allowance.Exhausted);
        Assert.Equal(PeriodEnd, allowance.PeriodEndsAt);
        await _usage.Received().GetTotalsAsync(PeriodStart, PeriodEnd, Team, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("4", true)]
    [InlineData("3.99", true)]
    [InlineData("4.01", false)]
    [InlineData("0", true)]
    public async Task TheAllowanceIsUsedUp_ExactlyWhenTheCostReachesTheLimit(string limit, bool exhausted)
    {
        _options.DefaultLimit = decimal.Parse(limit, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(exhausted, (await Source().GetAsync(Team)).Exhausted);
    }

    [Fact]
    public async Task ATeamsOwnLimit_BeatsTheDefault()
    {
        _limits.GetAsync(Team, Arg.Any<CancellationToken>()).Returns(new StoredSupportAssistantLimit { TeamKey = Team, Limit = 5m });

        Assert.Equal(0.8m, (await Source().GetAsync(Team)).Share);
    }

    [Fact]
    public async Task ATeamsOwnLimitOfNone_BeatsTheDefault()
    {
        _limits.GetAsync(Team, Arg.Any<CancellationToken>()).Returns(new StoredSupportAssistantLimit { TeamKey = Team, Limit = null });

        Assert.False((await Source().GetAsync(Team)).Applies);
    }

    [Fact]
    public async Task Units_AreTheCostDividedByWhatAUnitIsWorth_UnderTheHostsLabel()
    {
        _options.Display = SupportAllowanceDisplay.Units;
        _options.UnitValue = 0.001m;
        _options.UnitLabel = "WU";

        var allowance = await Source().GetAsync(Team);

        Assert.Equal(("WU", 10_000m, 4_000m), (allowance.Unit, allowance.Limit, allowance.Used));
    }

    [Fact]
    public async Task Money_IsShownInTheCataloguesCurrency()
    {
        _options.Display = SupportAllowanceDisplay.Money;

        var allowance = await Source().GetAsync(Team);

        Assert.Equal(("USD", 10m, 4m), (allowance.Unit, allowance.Limit, allowance.Used));
    }

    /// <summary>Only the share: no amount, in any unit, for a host that does not want a team to read one.</summary>
    [Fact]
    public async Task Share_ShowsNoAmountAtAll()
    {
        _options.Display = SupportAllowanceDisplay.Share;

        var allowance = await Source().GetAsync(Team);

        Assert.Equal((null, null, null, 0.4m), (allowance.Unit, allowance.Limit, allowance.Used, allowance.Share));
        Assert.All(allowance.Models, x => Assert.Null(x.CostPerAnswer));
    }

    /// <summary>Tokens are an estimate: the limit at the rate this team's answers cost so far.</summary>
    [Fact]
    public async Task Tokens_AreEstimatedAtTheRateTheTeamsAnswersCost()
    {
        _options.Display = SupportAllowanceDisplay.Tokens;

        var allowance = await Source().GetAsync(Team);

        Assert.Equal(("tokens", 100_000m, 40_000m), (allowance.Unit, allowance.Limit, allowance.Used));
    }

    [Fact]
    public async Task Tokens_WithNoUsageYet_ShowNoAmount()
    {
        _options.Display = SupportAllowanceDisplay.Tokens;
        _usage.GetTotalsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Team, Arg.Any<CancellationToken>()).Returns([]);

        var allowance = await Source().GetAsync(Team);

        Assert.Equal((null, null, 0m), (allowance.Limit, allowance.Used, allowance.Share));
    }

    /// <summary>10 spent of 10 is nothing left, and 0.1 an answer makes 60 questions of the 6 that remain.</summary>
    [Fact]
    public async Task QuestionsLeft_AreWhatRemainsDividedByWhatAnAnswerCostsOnThatModel()
    {
        _options.Display = SupportAllowanceDisplay.Money;

        var allowance = await Source().GetAsync(Team);

        var sonnet = allowance.Models.Single(x => x.ModelId == "sonnet");
        Assert.True(sonnet.Current);
        Assert.Equal((0.1m, 60L), (sonnet.CostPerAnswer, sonnet.QuestionsLeft));
    }

    /// <summary>A model the team has never used is estimated from what everyone's answers on it cost.</summary>
    [Fact]
    public async Task AModelTheTeamNeverUsed_IsEstimatedFromEveryonesAnswers()
    {
        _options.Display = SupportAllowanceDisplay.Money;
        _usage.GetTotalsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), null, Arg.Any<CancellationToken>())
            .Returns([new SupportAssistantUsageTotal { TeamKey = "other", Model = "mistral-large", Answers = 100, Cost = 1m }]);

        var large = (await Source().GetAsync(Team)).Models.Single(x => x.ModelId == "large");

        Assert.False(large.Current);
        Assert.Equal((0.01m, 600L), (large.CostPerAnswer, large.QuestionsLeft));
    }

    [Fact]
    public async Task AModelNobodyUsed_HasNoEstimate_NotAFreeOne()
    {
        _options.Display = SupportAllowanceDisplay.Money;
        _usage.GetTotalsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), null, Arg.Any<CancellationToken>()).Returns([]);

        var large = (await Source().GetAsync(Team)).Models.Single(x => x.ModelId == "large");

        Assert.Equal((null, null), (large.CostPerAnswer, large.QuestionsLeft));
    }

    [Fact]
    public async Task WithNoUsageStore_NobodyIsLimited()
        => Assert.False((await new DefaultSupportAssistantAllowanceSource(_options, new FixedClock(Now), null, _limits, _pricing, _catalogue).GetAsync(Team)).Applies);

    // ---- who reads and who changes ----

    [Fact]
    public async Task AnAdministrator_ReadsTheAmounts()
    {
        _options.Display = SupportAllowanceDisplay.Money;

        var allowance = await Read(Authorizer(member: true, manage: true));

        Assert.Equal((10m, 4m), (allowance.Limit, allowance.Used));
        Assert.NotNull(allowance.Models[0].CostPerAnswer);
    }

    /// <summary>The share and the state, so a member knows why the assistant stopped; never what the team spends.</summary>
    [Fact]
    public async Task AMember_ReadsTheShareAndNoAmount()
    {
        _options.Display = SupportAllowanceDisplay.Money;

        var allowance = await Read(Authorizer(member: true, manage: false));

        Assert.Equal((null, null, 0.4m), (allowance.Limit, allowance.Used, allowance.Share));
        Assert.All(allowance.Models, x => Assert.Null(x.CostPerAnswer));
        Assert.Equal(60L, allowance.Models.Single(x => x.ModelId == "sonnet").QuestionsLeft);
    }

    [Fact]
    public async Task ANonMember_IsRefused()
        => await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Read(Authorizer(member: false, manage: true)));

    private Task<SupportAssistantAllowance> Read(TeamAuthorizer authorizer)
        => new AuthorizationSupportAssistantAllowanceServiceDecorator(new SupportAssistantAllowanceService(Source()), authorizer).GetAllowanceAsync(Team);

    [Fact]
    public async Task SettingALimit_RequiresTheSystemScope_AndChangesNothingWithout_It()
    {
        var service = LimitService(Authorizer(member: true, manage: true, system: false));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SetLimitAsync(new SupportAssistantLimitRequest { TeamKey = Team, Limit = 50m }));

        await _limits.DidNotReceiveWithAnyArgs().SetAsync(default);
    }

    [Fact]
    public async Task ALimitIsSet_AndSaysItIsTheTeamsOwn()
    {
        var service = LimitService(Authorizer(member: false, manage: false, system: true));

        var limit = await service.SetLimitAsync(new SupportAssistantLimitRequest { TeamKey = Team, Limit = 50m });

        Assert.Equal((50m, true, 10m, "USD"), (limit.Limit, limit.IsOwn, limit.DefaultLimit, limit.Currency));
        await _limits.Received().SetAsync(Arg.Is<StoredSupportAssistantLimit>(x => x.TeamKey == Team && x.Limit == 50m && x.ChangedAt == Now && x.ChangedByIdentity == "staff"));
    }

    [Fact]
    public async Task ANegativeLimit_IsRefused()
    {
        var service = LimitService(Authorizer(member: false, manage: false, system: true));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetLimitAsync(new SupportAssistantLimitRequest { TeamKey = Team, Limit = -1m }));

        await _limits.DidNotReceiveWithAnyArgs().SetAsync(default);
    }

    [Fact]
    public async Task ClearingALimit_HoldsTheTeamToTheDefaultAgain()
    {
        var service = LimitService(Authorizer(member: false, manage: false, system: true));

        var limit = await service.ClearLimitAsync(Team);

        Assert.Equal((10m, false), (limit.Limit, limit.IsOwn));
        await _limits.Received().DeleteForTeamAsync(Team, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadingALimit_SaysWhichHolds()
    {
        _limits.GetAsync(Team, Arg.Any<CancellationToken>()).Returns(new StoredSupportAssistantLimit { TeamKey = Team, Limit = null });

        var limit = await LimitService(Authorizer(member: false, manage: false, system: true)).GetLimitAsync(Team);

        Assert.Equal((null, true), (limit.Limit, limit.IsOwn));
    }

    [Fact]
    public async Task PurgingATeam_RemovesItsOwnLimit()
    {
        _limits.DeleteForTeamAsync(Team, Arg.Any<CancellationToken>()).Returns(true);

        Assert.Equal(1, await new SupportAssistantLimitPurgeParticipant(_limits).PurgeTeamDataAsync(Team));
    }

    [Fact]
    public async Task Registered_TheServicesResolve_TheScopeIsASystemScope_AndPurgingIncludesTheLimit()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ISupportCaseStore>());
        services.AddSingleton(_usage);
        services.AddSingleton(_limits);
        services.AddSingleton(_pricing);
        services.AddSingleton(Accessor(member: true, manage: true, system: true));
        services.AddSingleton<TeamAuthorizer>();
        services.Configure<AuditOptions>(_ => { });
        services.AddSingleton<CompositeAuditLogger>();
        services.AddSingleton<IAuditEntryFactory>(new AuditEntryFactory(null));
        services.AddThargaSupportCases(o => o.Assistant.Allowance.DefaultLimit = 10m);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.True((await scope.ServiceProvider.GetRequiredService<ISupportAssistantAllowanceService>().GetAllowanceAsync(Team)).Applies);
        Assert.IsType<DefaultSupportAssistantAllowanceSource>(scope.ServiceProvider.GetRequiredService<ISupportAssistantAllowanceSource>());
        Assert.Equal(10m, (await scope.ServiceProvider.GetRequiredService<ISupportAssistantLimitService>().GetLimitAsync(Team)).DefaultLimit);
        Assert.Contains(scope.ServiceProvider.GetServices<ITeamPurgeParticipant>(), x => x is SupportAssistantLimitPurgeParticipant);
        Assert.Contains(SystemSupportScopes.AllowanceManage, scope.ServiceProvider.GetRequiredService<ISystemScopeRegistry>().All.Select(x => x.Name));
    }

    /// <summary>A host that counts more than the assistant's own usage registers its own source, and it wins.</summary>
    [Fact]
    public void AHostsOwnSource_Wins()
    {
        var own = Substitute.For<ISupportAssistantAllowanceSource>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ISupportCaseStore>());
        services.AddScoped(_ => own);
        services.AddThargaSupportCases();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Same(own, scope.ServiceProvider.GetRequiredService<ISupportAssistantAllowanceSource>());
    }

    private ISupportAssistantLimitService LimitService(TeamAuthorizer authorizer)
        => new AuthorizationSupportAssistantLimitServiceDecorator(
            new SupportAssistantLimitService(_limits, _options, new FixedClock(Now), authorizer, _catalogue),
            authorizer);

    private static TeamAuthorizer Authorizer(bool member, bool manage, bool system = false) => new(Accessor(member, manage, system));

    private static ITeamPrincipalAccessor Accessor(bool member, bool manage, bool system)
    {
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, "staff")];
        if (member) claims.Add(new Claim(TeamClaimTypes.TeamKey, Team));
        if (manage) claims.Add(new Claim(TeamClaimTypes.Scope, SupportScopes.AssistantManage));
        if (system) claims.Add(new Claim(TeamClaimTypes.SystemScope, SystemSupportScopes.AllowanceManage));
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(ValueTask.FromResult(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));
        return accessor;
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }
}
