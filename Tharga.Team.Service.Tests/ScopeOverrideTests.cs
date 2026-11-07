namespace Tharga.Team.Service.Tests;

/// <summary>
/// A host can change how a registered scope is granted, the toolkit's own included (Tharga/Team#405).
/// </summary>
/// <remarks>
/// <b>The point is order.</b> <c>Register</c> throws on a name a module registered first, and a host-side replace only
/// works when called after that module. An override is kept by name and applied on read, so every case here is asserted
/// both before and after the registration, and a name nobody registers is asserted to stop the application.
/// </remarks>
public class ScopeOverrideTests
{
    private static ScopeRegistry Toolkit()
    {
        var r = new ScopeRegistry();
        r.Register(TeamScopes.Read, AccessLevel.Viewer, "View team details and members.");
        r.Register(ApiKeyScopes.Manage, AccessLevel.Administrator, "Create, refresh, lock, and delete API keys.");
        r.Register(AuditScopes.Read, AccessLevel.Administrator, "View the audit log.");
        return r;
    }

    [Fact]
    public void ARelevel_ChangesWhoIsGrantedTheScope()
    {
        var registry = Toolkit();

        registry.Override(ApiKeyScopes.Manage, AccessLevel.User);

        Assert.Contains(ApiKeyScopes.Manage, registry.GetScopesForAccessLevel(AccessLevel.User));
        Assert.Equal(AccessLevel.User, registry.All.Single(s => s.Name == ApiKeyScopes.Manage).DefaultMinimumLevel);
    }

    [Fact]
    public void GrantOnly_RemovesTheScope_FromEveryLevel_OwnerAndAdministratorIncluded()
    {
        var registry = Toolkit();

        registry.Override(ApiKeyScopes.Manage, grantOnly: true);

        Assert.DoesNotContain(ApiKeyScopes.Manage, registry.GetScopesForAccessLevel(AccessLevel.Owner));
        Assert.DoesNotContain(ApiKeyScopes.Manage, registry.GetScopesForAccessLevel(AccessLevel.Administrator));
        Assert.DoesNotContain(ApiKeyScopes.Manage, registry.GetScopesForAccessLevel(AccessLevel.User));
        Assert.True(registry.All.Single(s => s.Name == ApiKeyScopes.Manage).GrantOnly);
    }

    [Fact]
    public void AGrantOnlyToolkitScope_IsStillHeld_ThroughAnOverrideOrARole()
    {
        var registry = Toolkit();
        registry.Override(ApiKeyScopes.Manage, grantOnly: true);

        var viaOverride = registry.GetEffectiveScopes(AccessLevel.Administrator, null, [ApiKeyScopes.Manage]);

        Assert.Contains(ApiKeyScopes.Manage, viaOverride);
        Assert.DoesNotContain(ApiKeyScopes.Manage, registry.GetEffectiveScopes(AccessLevel.Administrator, null));
    }

    /// <summary>The order trap in the issue: the override is given before the module registers the scope.</summary>
    [Fact]
    public void AnOverride_GivenBeforeTheScopeIsRegistered_StillApplies()
    {
        var registry = new ScopeRegistry();
        registry.Override(ApiKeyScopes.Manage, grantOnly: true);

        registry.Register(ApiKeyScopes.Manage, AccessLevel.Administrator, "Create, refresh, lock, and delete API keys.");

        Assert.DoesNotContain(ApiKeyScopes.Manage, registry.GetScopesForAccessLevel(AccessLevel.Administrator));
        Assert.Empty(registry.UnmatchedOverrides);
    }

    [Fact]
    public void AnOverride_GivenAfterTheScopeWasRead_StillApplies()
    {
        var registry = Toolkit();
        Assert.Contains(ApiKeyScopes.Manage, registry.GetScopesForAccessLevel(AccessLevel.Administrator));

        registry.Override(ApiKeyScopes.Manage, grantOnly: true);

        Assert.DoesNotContain(ApiKeyScopes.Manage, registry.GetScopesForAccessLevel(AccessLevel.Administrator));
    }

    [Fact]
    public void GroupWordingAndPrincipals_AreOverridable_AndOnlyWhatIsPassedChanges()
    {
        var registry = Toolkit();

        registry.Override(ApiKeyScopes.Manage, group: "api", appliesTo: ScopePrincipals.Members, description: "Manage the keys.");

        var scope = registry.All.Single(s => s.Name == ApiKeyScopes.Manage);
        Assert.Equal("api", scope.Group);
        Assert.Equal(ScopePrincipals.Members, scope.AppliesTo);
        Assert.Equal("Manage the keys.", scope.Description);
        Assert.Equal(AccessLevel.Administrator, scope.DefaultMinimumLevel);
        Assert.False(scope.GrantOnly);
    }

    [Fact]
    public void ANeighbouringScope_IsNotTouched()
    {
        var registry = Toolkit();

        registry.Override(ApiKeyScopes.Manage, grantOnly: true);

        Assert.Contains(AuditScopes.Read, registry.GetScopesForAccessLevel(AccessLevel.Administrator));
        Assert.Contains(TeamScopes.Read, registry.GetScopesForAccessLevel(AccessLevel.Viewer));
    }

    [Fact]
    public void ARepeatedOverride_AddsToTheEarlierOne_AndAValueGivenAgainReplacesIt()
    {
        var registry = Toolkit();

        registry.Override(AuditScopes.Read, group: "audit");
        registry.Override(AuditScopes.Read, AccessLevel.User);
        registry.Override(AuditScopes.Read, AccessLevel.Viewer);

        var scope = registry.All.Single(s => s.Name == AuditScopes.Read);
        Assert.Equal("audit", scope.Group);
        Assert.Equal(AccessLevel.Viewer, scope.DefaultMinimumLevel);
    }

    [Fact]
    public void MakingAGrantOnlyScopeOrdinary_NeedsALevel()
    {
        var registry = new ScopeRegistry();
        registry.RegisterGrantOnly("case:read");

        registry.Override("case:read", grantOnly: false);
        Assert.Throws<InvalidOperationException>(() => registry.All);

        registry.Override("case:read", AccessLevel.Administrator, grantOnly: false);
        Assert.Contains("case:read", registry.GetScopesForAccessLevel(AccessLevel.Administrator));
    }

    [Fact]
    public void GrantOnly_WithALevel_IsRefusedAtTheCall()
    {
        var registry = Toolkit();

        Assert.Throws<ArgumentException>(() => registry.Override(ApiKeyScopes.Manage, AccessLevel.User, grantOnly: true));
    }

    [Fact]
    public void AnOverride_WithNoName_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => new ScopeRegistry().Override(" ", AccessLevel.User));
    }

    [Fact]
    public void RegisteringTheSameNameTwice_StillThrows()
    {
        var registry = Toolkit();

        Assert.Throws<InvalidOperationException>(() => registry.Register(ApiKeyScopes.Manage, AccessLevel.User));
    }

    [Fact]
    public void ANameNobodyRegistered_IsListed()
    {
        var registry = Toolkit();

        registry.Override("apikey:mange", grantOnly: true);

        Assert.Equal(["apikey:mange"], registry.UnmatchedOverrides);
    }

    // ---- The startup check ----

    [Fact]
    public async Task TheCheck_StopsStartup_ForAScopeNobodyRegistered_NamingIt()
    {
        var registry = Toolkit();
        registry.Override("apikey:mange", grantOnly: true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new ScopeOverrideCheck(registry).StartAsync(default));

        Assert.Contains("apikey:mange", ex.Message);
    }

    [Fact]
    public async Task TheCheck_StopsStartup_ForAnOverrideThatCannotBeApplied()
    {
        var registry = new ScopeRegistry();
        registry.RegisterGrantOnly("case:read");
        registry.Override("case:read", grantOnly: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new ScopeOverrideCheck(registry).StartAsync(default));
    }

    [Fact]
    public async Task TheCheck_PassesWhenEveryOverrideHasItsScope()
    {
        var registry = Toolkit();
        registry.Override(ApiKeyScopes.Manage, grantOnly: true);

        await new ScopeOverrideCheck(registry).StartAsync(default);
    }

    [Fact]
    public async Task TheCheck_IsQuiet_ForAHostsOwnRegistry()
    {
        await new ScopeOverrideCheck(Substitute.For<IScopeRegistry>()).StartAsync(default);
    }
}
