using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The team services can actually be <b>constructed</b>, not merely registered.
/// </summary>
/// <remarks>
/// <b>Registration and resolution are different assertions, and only one of them was being made.</b>
/// <see cref="TeamServiceRegistrationCompletenessTests"/> checks that a descriptor exists for every facet —
/// which stays true however broken the factory behind it is, because a factory lambda is not run until
/// something resolves it.
/// <para>
/// That gap shipped: <c>AddThargaTeamBlazor</c> builds the management service with
/// <c>Activator.CreateInstance</c> and a positional argument list, and a seventh constructor parameter was
/// added with a default value. That is source-compatible everywhere else in C#, but <c>Activator</c> does
/// not fill in optional parameters — so every host with a member type died at its first resolve with
/// <c>MissingMethodException</c>, while the build was clean and the whole suite passed
/// (Tharga/Team#261, reported again as #265 against 3.20.1).
/// </para>
/// <para>
/// So this resolves each facet for real. It is the assertion that had to exist, and the reason it did not
/// is that the cheaper one looked like it covered the same ground.
/// </para>
/// </remarks>
public class TeamServiceResolutionTests
{
    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // What a Blazor host contributes and this package does not: the principal accessor is built on it.
        services.AddScoped<AuthenticationStateProvider, StubAuthenticationStateProvider>();

        services.AddThargaTeamBlazor(o => o.RegisterTeamService<StubTeamService, StubUserService, StubMember>());
        return services.BuildServiceProvider();
    }

    private sealed class StubAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    public static TheoryData<Type> Facets()
    {
        var data = new TheoryData<Type>();
        foreach (var facet in TeamServiceFacets.All) data.Add(facet);
        return data;
    }

    [Theory]
    [MemberData(nameof(Facets))]
    public void EveryFacet_Resolves(Type facet)
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetService(facet);

        Assert.NotNull(resolved);
    }

    /// <summary>
    /// The concrete type the facets all delegate to, and the one that actually threw. Asserted separately
    /// so a failure names the constructor rather than an interface that looks unrelated to it.
    /// </summary>
    [Fact]
    public void TheManagementService_Resolves()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetService(typeof(TeamManagementService<StubMember>));

        Assert.NotNull(resolved);
    }

    /// <summary>
    /// The invitation throttle is actually applied to the facet, not merely available to be applied.
    /// </summary>
    /// <remarks>
    /// Its own registration tests cover the extension method; what they cannot cover is the one line in
    /// <c>AddThargaTeamBlazor</c> that calls it — and the facets are registered with <c>TryAdd</c>, so a
    /// call placed wrongly would leave an unthrottled service that looks wired. Asserted by type name
    /// because the decorator is internal to <c>Tharga.Team.Service</c>.
    /// </remarks>
    [Fact]
    public void TheInvitationService_IsThrottled()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetRequiredService<ITeamInvitationService>();

        Assert.Equal("ThrottledTeamInvitationService", resolved.GetType().Name);
    }

    /// <summary>
    /// A theory over an empty set passes while checking nothing, and this file exists because something
    /// that looked covered was not.
    /// </summary>
    [Fact]
    public void TheGuard_ActuallyCoversSomething()
    {
        Assert.NotEmpty(TeamServiceFacets.All);
    }
}
