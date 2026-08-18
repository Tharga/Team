using Tharga.Team.Service;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The startup guard that turns a silent authorization failure into a loud boot failure.
/// </summary>
/// <remarks>
/// <b>Renaming a scope is invisible to the compiler.</b> A host maps scope names as strings — in
/// <c>ConfigureSystemRoles</c> or on a system API key — so <c>teams:assign-owner</c> keeps registering
/// happily after the rename and simply authorizes nothing. Without this check the first sign would be an
/// operator refused a capability they believe they hold, in production, with nothing in the logs saying
/// why.
/// <para>
/// The message is asserted rather than just the throw: a guard that fires without naming the replacement
/// leaves the host exactly as stuck as no guard at all.
/// </para>
/// </remarks>
public class RetiredScopeCheckTests
{
    private static Task RunAsync(params string[] registeredScopes)
    {
        var registry = new SystemScopeRegistry();
        foreach (var scope in registeredScopes) registry.Register(scope);

        return new RetiredScopeCheck(registry).StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TheRetiredScope_FailsStartup()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync("teams:assign-owner"));

        Assert.Contains("teams:assign-owner", ex.Message);
        Assert.Contains(SystemTeamScopes.SetOwner, ex.Message);
    }

    [Fact]
    public async Task TheRetiredScope_FailsEvenAlongsideTheReplacement()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunAsync(SystemTeamScopes.SetOwner, "teams:assign-owner"));
    }

    [Fact]
    public async Task TheReplacementAlone_Passes()
    {
        await RunAsync(SystemTeamScopes.SetOwner);
    }

    [Fact]
    public async Task AnEmptyRegistry_Passes()
    {
        await RunAsync();
    }

    [Fact]
    public async Task UnrelatedScopes_Pass()
    {
        await RunAsync(SystemTeamScopes.Delete, SystemTeamScopes.Purge, SystemUserScopes.Manage);
    }
}
