using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The host's rule for who may work in a team: asked where the claims are built, cached briefly per user, session and team, refused teams not
/// issued, a failing guard handled as configured, refusals audited, and every bit of it absent when no guard is registered.
/// </summary>
/// <remarks>Teams and users are made up; none is built into the package.</remarks>
public class TeamEntryGuardTests
{
    private const string TeamKey = "acme";
    private const string OtherTeam = "globex";
    private const string UserKey = "user-1";

    private readonly StepClock _time = new();
    private readonly TeamEntryCache _cache;
    private readonly List<AuditEntry> _audit = [];
    private readonly Mock<ITeamEntryGuard> _guard = new();
    private readonly TeamEntryOptions _entryOptions = new();

    public TeamEntryGuardTests()
    {
        _cache = new TeamEntryCache(_time);
        _guard.Setup(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(TeamEntryDecision.Allow);
    }

    private TeamEntryEvaluator Evaluator(bool withGuard = true)
        => new(_cache, Options.Create(new ThargaBlazorOptions { TeamEntry = _entryOptions }), withGuard ? _guard.Object : null, null,
            new CompositeAuditLogger([new Collecting(_audit)], Options.Create(new AuditOptions())), new AuditEntryFactory(null));

    private static ClaimsPrincipal Principal(string session = "session-1", params Claim[] extra)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserKey), new Claim("sid", session), .. extra], "Test"));

    private void GuardSays(TeamEntryDecision decision)
        => _guard.Setup(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(decision);

    // ---- nothing registered

    [Fact]
    public async Task WithNoGuardRegistered_EveryTeamIsAllowed_WithNoLookupAndNoAudit()
    {
        var evaluator = Evaluator(withGuard: false);

        var decision = await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);

        Assert.False(evaluator.Active);
        Assert.True(decision.Allowed);
        Assert.Empty(_audit);
    }

    [Fact]
    public async Task SwitchedOff_TheGuardIsNotAsked_AndEveryTeamIsAllowed()
    {
        _entryOptions.Enabled = false;
        GuardSays(TeamEntryDecision.Deny("No."));
        var evaluator = Evaluator();

        var decision = await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);

        Assert.False(evaluator.Active);
        Assert.True(decision.Allowed);
        _guard.VerifyNoOtherCalls();
    }

    // ---- the answers

    [Fact]
    public async Task TheGuardsAnswer_IsPassedOn_AllowDenyAndStepUp()
    {
        var evaluator = Evaluator();

        GuardSays(TeamEntryDecision.Allow);
        Assert.True((await evaluator.EvaluateAsync(Principal("a"), UserKey, TeamKey)).Allowed);

        GuardSays(TeamEntryDecision.Deny("Contract ended."));
        var denied = await evaluator.EvaluateAsync(Principal("b"), UserKey, TeamKey);
        Assert.Equal((TeamEntryOutcome.Deny, "Contract ended."), (denied.Outcome, denied.Reason));

        GuardSays(TeamEntryDecision.StepUp("https://id.example/step-up?return={returnUrl}", "Sign in with BankID."));
        var stepUp = await evaluator.EvaluateAsync(Principal("c"), UserKey, TeamKey);
        Assert.Equal(TeamEntryOutcome.StepUp, stepUp.Outcome);
        Assert.Equal("https://id.example/step-up?return={returnUrl}", stepUp.StepUpUrl);
    }

    [Fact]
    public async Task TheGuardIsGivenTheCaller_AndTheTeam()
    {
        var principal = Principal();

        await Evaluator().EvaluateAsync(principal, UserKey, TeamKey);

        _guard.Verify(x => x.CheckAsync(principal, TeamKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AStepUpWithNoAddress_IsRefusedAsAFailure_NotSentNowhere()
    {
        GuardSays(new TeamEntryDecision { Outcome = TeamEntryOutcome.StepUp });

        var decision = await Evaluator().EvaluateAsync(Principal(), UserKey, TeamKey);

        Assert.Equal(TeamEntryOutcome.Deny, decision.Outcome);
        Assert.Equal(TeamEntryEvaluator.ErrorReason, decision.Reason);
    }

    // ---- the cache

    [Fact]
    public async Task AnAnswer_IsRemembered_ForTheCacheDuration_ThenAsked_Again()
    {
        var evaluator = Evaluator();

        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);
        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);
        _time.Advance(_entryOptions.CacheDuration - TimeSpan.FromSeconds(1));
        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);
        _guard.Verify(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        _time.Advance(TimeSpan.FromSeconds(2));
        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);
        _guard.Verify(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ACacheDurationOfZero_AsksTheGuardEveryTime()
    {
        _entryOptions.CacheDuration = TimeSpan.Zero;
        var evaluator = Evaluator();

        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);
        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);

        _guard.Verify(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task TwoSessionsOfOnePerson_AreNeverShared_ButTheSameSessionIs()
    {
        var evaluator = Evaluator();

        await evaluator.EvaluateAsync(Principal("browser-1"), UserKey, TeamKey);
        await evaluator.EvaluateAsync(Principal("browser-2"), UserKey, TeamKey);
        await evaluator.EvaluateAsync(Principal("browser-1"), UserKey, TeamKey);

        _guard.Verify(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), TeamKey, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task WithNoSessionId_WhatTheSignInProved_TellsSessionsApart()
    {
        var evaluator = Evaluator();
        ClaimsPrincipal Bare(string amr) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserKey), new Claim("auth_time", "1000"), new Claim("amr", amr)], "Test"));

        await evaluator.EvaluateAsync(Bare("pwd"), UserKey, TeamKey);
        await evaluator.EvaluateAsync(Bare("bankid"), UserKey, TeamKey);

        _guard.Verify(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), TeamKey, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task EachTeam_IsAskedAbout_OnItsOwn()
    {
        var evaluator = Evaluator();
        GuardSays(TeamEntryDecision.Allow);

        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);
        await evaluator.EvaluateAsync(Principal(), UserKey, OtherTeam);

        _guard.Verify(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ---- telling the library an answer changed

    [Fact]
    public async Task AStepUp_IsNeverAnsweredFromTheCache_SoOneTakenOnAnotherInstanceCountsAtOnce()
    {
        var evaluator = Evaluator();
        GuardSays(TeamEntryDecision.StepUp("https://id.example/step-up"));
        Assert.Equal(TeamEntryOutcome.StepUp, (await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey)).Outcome);

        GuardSays(TeamEntryDecision.Allow);

        Assert.True((await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey)).Allowed);
        _guard.Verify(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), TeamKey, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task AnAllowAfterAStepUp_IsRememberedAsUsual()
    {
        var evaluator = Evaluator();
        GuardSays(TeamEntryDecision.StepUp("https://id.example/step-up"));
        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);
        GuardSays(TeamEntryDecision.Allow);
        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);

        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);

        _guard.Verify(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), TeamKey, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ADeny_IsStillRemembered_ForTheCacheDuration()
    {
        var evaluator = Evaluator();
        GuardSays(TeamEntryDecision.Deny("Contract ended."));
        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);
        GuardSays(TeamEntryDecision.Allow);

        Assert.False((await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey)).Allowed);
        _guard.Verify(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), TeamKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnAnswerThatChanged_IsAskedAgain_OnceTheHostSaysSo()
    {
        var evaluator = Evaluator();
        GuardSays(TeamEntryDecision.Deny("Contract ended."));
        Assert.False((await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey)).Allowed);

        GuardSays(TeamEntryDecision.Allow);
        Assert.False((await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey)).Allowed);

        await ((ITeamEntryGuardState)_cache).InvalidateAsync(UserKey, TeamKey);

        Assert.True((await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey)).Allowed);
    }

    [Fact]
    public async Task InvalidatingOneTeam_LeavesTheOthersAnswered()
    {
        var evaluator = Evaluator();
        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);
        await evaluator.EvaluateAsync(Principal(), UserKey, OtherTeam);

        await _cache.InvalidateAsync(UserKey, TeamKey);
        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);
        await evaluator.EvaluateAsync(Principal(), UserKey, OtherTeam);

        _guard.Verify(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), TeamKey, It.IsAny<CancellationToken>()), Times.Exactly(2));
        _guard.Verify(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), OtherTeam, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidatingAUser_ForgetsEveryTeam_AndTellsListeners()
    {
        var evaluator = Evaluator();
        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);
        await evaluator.EvaluateAsync(Principal(), UserKey, OtherTeam);
        var heard = new List<(string User, string Team)>();
        _cache.Invalidated += (_, e) => heard.Add((e.UserKey, e.TeamKey));

        await _cache.InvalidateAsync(UserKey);
        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);
        await evaluator.EvaluateAsync(Principal(), UserKey, OtherTeam);

        Assert.Equal([(UserKey, (string)null)], heard);
        _guard.Verify(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    // ---- the audit

    [Fact]
    public async Task ARefusal_IsAudited_WithItsReason_OncePerCacheWindow()
    {
        GuardSays(TeamEntryDecision.Deny("Contract ended."));
        var evaluator = Evaluator();

        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);
        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);
        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);

        var entry = Assert.Single(_audit);
        Assert.Equal(("team", "entry-denied", false), (entry.Feature, entry.Action, entry.Success));
        Assert.Equal(TeamKey, entry.TeamKey);
        Assert.Equal("Contract ended.", entry.Metadata["team.entry.reason"]);
        Assert.Equal("Deny", entry.Metadata["team.entry.outcome"]);
    }

    [Fact]
    public async Task AStepUp_IsAudited_OncePerCacheWindow_ThoughTheGuardIsAskedEachTime()
    {
        GuardSays(TeamEntryDecision.StepUp("https://id.example/step-up", "Sign in with BankID."));
        var evaluator = Evaluator();

        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);
        _time.Advance(_entryOptions.CacheDuration - TimeSpan.FromSeconds(1));
        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);
        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);

        var entry = Assert.Single(_audit);
        Assert.Equal("StepUp", entry.Metadata["team.entry.outcome"]);
        _guard.Verify(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), TeamKey, It.IsAny<CancellationToken>()), Times.Exactly(3));

        _time.Advance(TimeSpan.FromSeconds(2));
        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);

        Assert.Equal(2, _audit.Count);
    }

    [Fact]
    public async Task ADifferentStepUp_IsAuditedAtOnce()
    {
        var evaluator = Evaluator();
        GuardSays(TeamEntryDecision.StepUp("https://id.example/connect", "Connect BankID."));
        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);

        GuardSays(TeamEntryDecision.StepUp("https://id.example/sign", "Sign in with BankID."));
        await evaluator.EvaluateAsync(Principal(), UserKey, TeamKey);

        Assert.Equal(["Connect BankID.", "Sign in with BankID."], _audit.Select(x => x.Metadata["team.entry.reason"]));
    }

    [Fact]
    public async Task AnAllowedEntry_LeavesNoAuditEntry()
    {
        await Evaluator().EvaluateAsync(Principal(), UserKey, TeamKey);

        Assert.Empty(_audit);
    }

    // ---- a guard that fails

    [Fact]
    public async Task AGuardThatThrows_DeniesByDefault_AuditsWhy_AndTellsTheCallerOnlyThatItCouldNotBeChecked()
    {
        _guard.Setup(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("the verification store is down"));

        var decision = await Evaluator().EvaluateAsync(Principal(), UserKey, TeamKey);

        Assert.Equal(TeamEntryOutcome.Deny, decision.Outcome);
        Assert.Equal(TeamEntryEvaluator.ErrorReason, decision.Reason);
        Assert.DoesNotContain("verification store", decision.Reason);
        Assert.Contains(_audit, x => x.ErrorMessage == "the verification store is down");
    }

    [Fact]
    public async Task AGuardThatThrows_CanBeSetToAllow_ForAGuardThatDependsOnSomethingThatCanBeDown()
    {
        _entryOptions.OnError = TeamEntryErrorBehavior.Allow;
        _guard.Setup(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("down"));

        var decision = await Evaluator().EvaluateAsync(Principal(), UserKey, TeamKey);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task AGuardThatReturnsNothing_IsAFailure()
    {
        _guard.Setup(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((TeamEntryDecision)null);

        Assert.Equal(TeamEntryOutcome.Deny, (await Evaluator().EvaluateAsync(Principal(), UserKey, TeamKey)).Outcome);
    }

    [Fact]
    public async Task ACancelledCheck_IsNotMistakenForAFailure()
    {
        _guard.Setup(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => Evaluator().EvaluateAsync(Principal(), UserKey, TeamKey));
    }

    // ---- where the claims are built

    private sealed record Member(string Key, AccessLevel? AccessLevel) : ITeamMember
    {
        public string Name => "Alice";
        public Invitation Invitation => null;
        public DateTime? LastSeen => null;
        public MembershipState? State => MembershipState.Member;
        public string[] TenantRoles => [];
        public string[] ScopeOverrides => [];
    }

    private TeamMembershipClaimsBuilder Builder(bool member = true, bool withGuard = true)
    {
        var teamService = new Mock<ITeamService>();
        teamService.Setup(x => x.GetTeamMemberAsync(TeamKey, UserKey)).ReturnsAsync(member ? new Member("member-1", AccessLevel.Owner) : null);
        teamService.Setup(x => x.GetConsentedTeamsAsync(It.IsAny<string[]>())).Returns(AsyncEnumerable.Empty<ITeam>());

        var userService = new Mock<IUserService>();
        userService.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(Mock.Of<IUser>(u => u.Key == UserKey));
        userService.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<bool>())).ReturnsAsync(Mock.Of<IUser>(u => u.Key == UserKey));

        var scopes = new Mock<IScopeRegistry>();
        scopes.Setup(x => x.GetEffectiveScopes(It.IsAny<AccessLevel>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>())).Returns(["orders:read"]);

        return new TeamMembershipClaimsBuilder(teamService.Object, userService.Object, Options.Create(new ThargaBlazorOptions { TeamEntry = _entryOptions }), scopes.Object, null,
            withGuard ? Evaluator() : null);
    }

    [Fact]
    public async Task AllowedEntry_IssuesTheTeamsClaims_AsItAlwaysDid()
    {
        var claims = await Builder().BuildAsync(Principal(), TeamKey);

        Assert.Contains(claims, x => x.Type == TeamClaimTypes.TeamKey && x.Value == TeamKey);
        Assert.Contains(claims, x => x.Type == TeamClaimTypes.Scope && x.Value == "orders:read");
    }

    [Fact]
    public async Task ARefusedTeam_IsNotIssued_SoTheCallerHasNoTeamRatherThanOneTheyMayNotUse()
    {
        GuardSays(TeamEntryDecision.Deny("Contract ended."));

        var claims = await Builder().BuildAsync(Principal(), TeamKey);

        Assert.Empty(claims);
    }

    [Fact]
    public async Task AStepUp_IsNotIssuedEither_UntilTheGuardAllows()
    {
        GuardSays(TeamEntryDecision.StepUp("https://id.example/step-up"));
        var builder = Builder();
        Assert.Empty(await builder.BuildAsync(Principal(), TeamKey));

        GuardSays(TeamEntryDecision.Allow);
        await _cache.InvalidateAsync(UserKey);

        Assert.NotEmpty(await builder.BuildAsync(Principal(), TeamKey));
    }

    [Fact]
    public async Task AnyPathThatBuildsClaims_IsRefused_NotJustTheSelector()
    {
        // The selected team travels in a cookie the browser can set by hand, and the transformation builds from it. The refusal is in the builder.
        GuardSays(TeamEntryDecision.Deny("Contract ended."));
        var builder = Builder();

        Assert.Empty(await builder.BuildAsync(Principal(), TeamKey));
        Assert.Empty(await builder.BuildAsync(Principal(), TeamKey, recordActivity: false));
    }

    [Fact]
    public async Task SomebodyWhoIsNoMemberOfTheTeam_IsNeverAskedAbout_AndGetsNothing()
    {
        var claims = await Builder(member: false).BuildAsync(Principal(), TeamKey);

        Assert.Empty(claims);
        _guard.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WithNoGuard_TheClaimsAreExactlyWhatTheyWere()
    {
        var without = await Builder(withGuard: false).BuildAsync(Principal(), TeamKey);
        var with = await Builder().BuildAsync(Principal(), TeamKey);

        Assert.Equal(without.Select(x => (x.Type, x.Value)).Order(), with.Select(x => (x.Type, x.Value)).Order());
    }

    // ---- the circuit's revalidation

    private TeamClaimRevalidator Revalidator()
        => new(Builder(), null);

    private static ClaimsPrincipal InCircuit(params string[] teamClaims)
        => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, UserKey),
            new Claim("sid", "session-1"),
            new Claim(Constants.TeamKeyCookie, TeamKey),
            .. teamClaims.Select(x => new Claim(TeamClaimTypes.Scope, x))
        ], "Test"));

    [Fact]
    public async Task AnAnswerThatTurnsToDeny_TakesTheTeamsClaimsAwayFromAnOpenCircuit()
    {
        var revalidator = Revalidator();
        var allowed = await revalidator.TryRefreshAsync(InCircuit());
        Assert.NotNull(allowed);
        Assert.Contains(allowed.Claims, x => x.Type == TeamClaimTypes.TeamKey);

        GuardSays(TeamEntryDecision.Deny("Contract ended."));
        await _cache.InvalidateAsync(UserKey);

        var refused = await revalidator.TryRefreshAsync(allowed);

        Assert.NotNull(refused);
        Assert.DoesNotContain(refused.Claims, x => x.Type == TeamClaimTypes.TeamKey);
        Assert.DoesNotContain(refused.Claims, x => x.Type == TeamClaimTypes.Scope);
        Assert.Contains(refused.Claims, x => x.Type == Constants.TeamKeyCookie && x.Value == TeamKey);
    }

    [Fact]
    public async Task AfterAStepUp_TheMarkerStillNamesTheTeam_SoTheCircuitGetsItBack()
    {
        GuardSays(TeamEntryDecision.StepUp("https://id.example/step-up"));
        var revalidator = Revalidator();
        var refused = await revalidator.TryRefreshAsync(InCircuit("orders:read")) ?? InCircuit();

        GuardSays(TeamEntryDecision.Allow);
        await _cache.InvalidateAsync(UserKey);
        var back = await revalidator.TryRefreshAsync(refused);

        Assert.NotNull(back);
        Assert.Contains(back.Claims, x => x.Type == TeamClaimTypes.TeamKey && x.Value == TeamKey);
    }

    // ---- the browser

    private sealed class FakeNavigation : NavigationManager
    {
        public FakeNavigation() => Initialize("https://localhost/", "https://localhost/orders/7?tab=2");

        public string Navigated { get; private set; }

        public bool ForceLoad { get; private set; }

        public int Refreshes { get; private set; }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Navigated = uri;
            ForceLoad = options.ForceLoad;
        }

        public override void Refresh(bool forceReload = false) => Refreshes++;
    }

    private sealed class FakeAuthState(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal));
    }

    private (TeamEntryService Service, FakeNavigation Navigation) EntryService(ClaimsPrincipal principal = null)
    {
        var navigation = new FakeNavigation();
        var users = new Mock<IUserService>();
        users.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<bool>())).ReturnsAsync(Mock.Of<IUser>(u => u.Key == UserKey));

        return (new TeamEntryService(Evaluator(), new FakeAuthState(principal ?? Principal()), navigation, users.Object), navigation);
    }

    [Fact]
    public async Task ARefusedCheck_IsHeldAsTheRefusalToShow_AndClearedByAnAllowedOne()
    {
        var (service, _) = EntryService();
        var raised = new List<TeamEntryRefusal>();
        service.RefusalChanged += (_, r) => raised.Add(r);

        GuardSays(TeamEntryDecision.Deny("Contract ended."));
        await service.CheckAsync(TeamKey);
        Assert.Equal(("acme", "Contract ended."), (service.Refusal.TeamKey, service.Refusal.Decision.Reason));

        GuardSays(TeamEntryDecision.Allow);
        await _cache.InvalidateAsync(UserKey);
        await service.CheckAsync(OtherTeam);

        Assert.Null(service.Refusal);
        Assert.Equal(2, raised.Count);
        Assert.Null(raised[1]);
    }

    [Fact]
    public async Task WithNoGuard_TheBrowserIsNeverToldAnything()
    {
        var navigation = new FakeNavigation();
        var service = new TeamEntryService(Evaluator(withGuard: false), new FakeAuthState(Principal()), navigation);

        Assert.True((await service.CheckAsync(TeamKey)).Allowed);
        Assert.Null(service.Refusal);
    }

    [Fact]
    public async Task ACallerWhoIsNotSignedIn_IsNotRefused()
    {
        GuardSays(TeamEntryDecision.Deny("No."));
        var (service, _) = EntryService(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.True((await service.CheckAsync(TeamKey)).Allowed);
    }

    [Fact]
    public async Task AStepUp_SendsTheBrowserThere_WithThePageItWasOnToComeBackTo()
    {
        GuardSays(TeamEntryDecision.StepUp("https://id.example/step-up?return={returnUrl}", "Sign in with BankID."));
        var (service, navigation) = EntryService();
        await service.CheckAsync(TeamKey);

        service.StepUp(service.Refusal);

        Assert.Equal("https://id.example/step-up?return=%2Forders%2F7%3Ftab%3D2", navigation.Navigated);
        Assert.True(navigation.ForceLoad);
    }

    [Fact]
    public async Task AStepUpAddressWithNoPlaceholder_IsUsedAsItIs()
    {
        GuardSays(TeamEntryDecision.StepUp("https://id.example/step-up"));
        var (service, navigation) = EntryService();
        await service.CheckAsync(TeamKey);

        service.StepUp(service.Refusal);

        Assert.Equal("https://id.example/step-up", navigation.Navigated);
    }

    [Fact]
    public async Task ADeny_HasNowhereToSendTheBrowser()
    {
        GuardSays(TeamEntryDecision.Deny("Contract ended."));
        var (service, navigation) = EntryService();
        await service.CheckAsync(TeamKey);

        service.StepUp(service.Refusal);

        Assert.Null(navigation.Navigated);
    }

    // ---- choosing a team

    private sealed record FakeTeam(string Key, string Name) : ITeam
    {
        public string Icon => null;
        public string TeamKey => Key;
        public DateTime? LastSeen => null;
        public string[] ConsentedRoles => [];
        public AccessLevel? ConsentAccessLevel => null;
        public IReadOnlyList<ITeamMember> Members => [];
    }

    private (TeamStateService State, List<string> Scripts, FakeNavigation Navigation, TeamEntryService Entry) StateService(string currentTeamKey = null, params ITeam[] teams)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, UserKey), new Claim("sid", "session-1"), .. currentTeamKey == null ? [] : new[] { new Claim(Constants.TeamKeyCookie, currentTeamKey) }], "Test"));
        var teamService = new Mock<ITeamService>();
        teamService.Setup(x => x.GetTeamPageAsync(It.IsAny<TeamPageRequest>())).ReturnsAsync(() => new TeamPage([.. teams.Select(TeamSummary.Of)], null));
        var scripts = new List<string>();
        var js = new Mock<Microsoft.JSInterop.IJSRuntime>();
        js.Setup(x => x.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>("eval", It.IsAny<object[]>()))
            .Callback<string, object[]>((_, args) => scripts.Add((string)args[0]))
            .Returns(ValueTask.FromResult<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(null));
        var local = new Mock<Blazored.LocalStorage.ILocalStorageService>();
        local.Setup(x => x.GetItemAsStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string)null);
        var (entry, navigation) = EntryService(principal);
        var state = new TeamStateService(teamService.Object, navigation, local.Object, js.Object, new FakeAuthState(principal), Options.Create(new ThargaBlazorOptions()), null, entry);

        return (state, scripts, navigation, entry);
    }

    private static async IAsyncEnumerable<ITeam> Async(ITeam[] teams)
    {
        await Task.CompletedTask;
        foreach (var team in teams) yield return team;
    }

    [Fact]
    public async Task ARefusedTeamTheCookieNames_IsNotTheSelection_AndNothingIsSetOrReloaded()
    {
        GuardSays(TeamEntryDecision.Deny("Contract ended."));
        var (state, scripts, navigation, entry) = StateService(TeamKey, new FakeTeam(TeamKey, "Acme"));

        var selected = await state.GetSelectedTeamAsync();

        Assert.Null(selected);
        Assert.False(state.TryGetSelectedTeam(out _));
        Assert.Empty(scripts);
        Assert.Equal(0, navigation.Refreshes);
        Assert.Equal("Contract ended.", entry.Refusal.Decision.Reason);
    }

    [Fact]
    public async Task AnAllowedTeam_IsTheSelection_AsAlways()
    {
        var (state, _, _, entry) = StateService(TeamKey, new FakeTeam(TeamKey, "Acme"));

        var selected = await state.GetSelectedTeamAsync();

        Assert.Equal(TeamKey, selected.Key);
        Assert.Null(entry.Refusal);
    }

    [Fact]
    public async Task ChoosingARefusedTeam_DoesNotSelectIt_NoCookieNoReload()
    {
        GuardSays(TeamEntryDecision.Deny("Contract ended."));
        var (state, scripts, navigation, entry) = StateService(null, new FakeTeam(TeamKey, "Acme"));

        await state.SetSelectedTeamAsync(new FakeTeam(TeamKey, "Acme"));

        Assert.False(state.TryGetSelectedTeam(out _));
        Assert.Empty(scripts);
        Assert.Equal(0, navigation.Refreshes);
        Assert.Null(navigation.Navigated);
        Assert.Equal(TeamEntryOutcome.Deny, entry.Refusal.Decision.Outcome);
    }

    [Fact]
    public async Task ChoosingATeamThatNeedsAStepUp_RemembersTheChoice_AndSendsTheBrowserToDoIt()
    {
        GuardSays(TeamEntryDecision.StepUp("https://id.example/step-up?return={returnUrl}"));
        var (state, scripts, navigation, _) = StateService(null, new FakeTeam(TeamKey, "Acme"));

        await state.SetSelectedTeamAsync(new FakeTeam(TeamKey, "Acme"));

        Assert.Contains(scripts, x => x.Contains($"{Constants.SelectedTeamKeyCookie}={TeamKey}"));
        Assert.StartsWith("https://id.example/step-up?return=", navigation.Navigated);
        Assert.False(state.TryGetSelectedTeam(out _));
    }

    [Fact]
    public async Task ChoosingAnAllowedTeam_IsAsItAlwaysWas()
    {
        var (state, scripts, navigation, _) = StateService(null, new FakeTeam(TeamKey, "Acme"));

        await state.SetSelectedTeamAsync(new FakeTeam(TeamKey, "Acme"));

        Assert.True(state.TryGetSelectedTeam(out var selected));
        Assert.Equal(TeamKey, selected.Key);
        Assert.Contains(scripts, x => x.Contains(TeamKey));
        Assert.Equal(1, navigation.Refreshes);
    }

    // ---- the circuit, when the host says an answer changed

    [Fact]
    public async Task AHostThatInvalidates_HasTheOpenCircuitBringItsClaimsUpToDateAtOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<AuthenticationStateProvider, Microsoft.AspNetCore.Components.Server.ServerAuthenticationStateProvider>();
        services.AddScoped<IHostEnvironmentAuthenticationStateProvider>(sp => (Microsoft.AspNetCore.Components.Server.ServerAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());
        services.AddThargaTeamBlazor(o => o.RegisterTeamService<StubTeamService, StubUserService>());

        var teamService = new Mock<ITeamService>();
        teamService.Setup(x => x.GetTeamMemberAsync(TeamKey, UserKey)).ReturnsAsync(new Member("member-1", AccessLevel.Owner));
        teamService.Setup(x => x.GetConsentedTeamsAsync(It.IsAny<string[]>())).Returns(AsyncEnumerable.Empty<ITeam>());
        var users = new Mock<IUserService>();
        users.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(Mock.Of<IUser>(u => u.Key == UserKey));
        users.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<bool>())).ReturnsAsync(Mock.Of<IUser>(u => u.Key == UserKey));
        var scopes = new Mock<IScopeRegistry>();
        scopes.Setup(x => x.GetEffectiveScopes(It.IsAny<AccessLevel>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>())).Returns(["orders:read"]);
        var current = TeamEntryDecision.Deny("Contract ended.");
        var guard = new Mock<ITeamEntryGuard>();
        guard.Setup(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => current);
        services.AddScoped(_ => teamService.Object);
        services.AddScoped(_ => users.Object);
        services.AddScoped(_ => scopes.Object);
        services.AddScoped(_ => guard.Object);

        using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var auth = (TeamRevalidatingAuthenticationStateProvider)scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
        scope.ServiceProvider.GetRequiredService<IHostEnvironmentAuthenticationStateProvider>().SetAuthenticationState(Task.FromResult(new AuthenticationState(InCircuit())));
        var changed = new TaskCompletionSource<ClaimsPrincipal>();
        auth.AuthenticationStateChanged += async task => changed.TrySetResult((await task).User);

        current = TeamEntryDecision.Allow;
        await provider.GetRequiredService<ITeamEntryGuardState>().InvalidateAsync(UserKey, TeamKey);

        var refreshed = await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Contains(refreshed.Claims, x => x.Type == TeamClaimTypes.TeamKey && x.Value == TeamKey);
    }

    [Fact]
    public void TheGuardsStateIsRegistered_AndIsTheCacheTheClaimsAreBuiltFrom()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<AuthenticationStateProvider, Microsoft.AspNetCore.Components.Server.ServerAuthenticationStateProvider>();
        services.AddScoped<IHostEnvironmentAuthenticationStateProvider>(sp => (Microsoft.AspNetCore.Components.Server.ServerAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());
        services.AddScoped<NavigationManager>(_ => new FakeNavigation());
        services.AddThargaTeamBlazor(o => o.RegisterTeamService<StubTeamService, StubUserService>());

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Assert.Same(provider.GetRequiredService<ITeamEntryGuardState>(), provider.GetRequiredService<TeamEntryCache>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ITeamEntryService>());
        Assert.False(scope.ServiceProvider.GetRequiredService<TeamEntryEvaluator>().Active);
    }

    private sealed class StepClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class Collecting(List<AuditEntry> entries) : IAuditLogger
    {
        public void Log(AuditEntry entry) => entries.Add(entry);

        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }
}
