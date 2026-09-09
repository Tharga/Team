using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Every service this package's own components inject can be resolved from the container this package
/// registers.
/// </summary>
/// <remarks>
/// <b>A Blazor <c>@inject</c> is resolved at render time, so a missing registration is not a startup
/// failure — it is a page that dies when somebody opens it.</b> Container validation does not reach it, and
/// neither does any test that only checks the service collection: nothing connects "this component needs X"
/// to "the registration provides X".
/// <para>
/// That gap shipped as Tharga/Team#266. Both icon dialogs inject <c>IIconProcessor</c> as required;
/// <c>AddThargaTeamBlazor</c> never registered one. The no-op default existed and was registered by
/// <c>Tharga.Team.MongoDB</c> — the wrong package to own it, since it is not the one whose components need
/// it and a host is not obliged to use it. Opening **Set icon** terminated the circuit, and to a user the
/// menu item simply did nothing.
/// </para>
/// <para>
/// <b>Types from optional packages are excluded, and that is a real hole rather than a tidy boundary.</b>
/// A component injecting a support-module service still dies where the host has not added that package. The
/// difference is only that the fix there is a decision — ship the seam, or make the component conditional —
/// rather than a missing line. See the exclusion list below.
/// </para>
/// </remarks>
public class ComponentDependencyResolutionTests
{
    /// <summary>
    /// Services that ship in a separate optional package, so this package cannot register them. Listed by
    /// name rather than inferred, so adding one is a visible decision.
    /// </summary>
    private static readonly string[] FromOptionalPackages =
    [
        "ISupportCaseService",
        "ISupportCaseNotifier"
    ];

    /// <summary>Services the host supplies by construction — its own storage types and member type.</summary>
    private static readonly string[] FromTheHost =
    [
        "IIconStore"
    ];

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // The three a Blazor host contributes and this package neither owns nor should register.
        services.AddScoped<AuthenticationStateProvider, StubAuthenticationStateProvider>();
        services.AddScoped<NavigationManager, StubNavigationManager>();
        services.AddScoped<Microsoft.JSInterop.IJSRuntime, StubJsRuntime>();
        services.AddThargaTeamBlazor(o => o.RegisterTeamService<StubTeamService, StubUserService, StubMember>());
        return services.BuildServiceProvider();
    }

    public static TheoryData<Type> InjectedThargaServices()
    {
        var data = new TheoryData<Type>();
        foreach (var type in InjectedTypes()) data.Add(type);
        return data;
    }

    private static Type[] InjectedTypes()
        => typeof(ThargaBlazorOptions).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(typeof(IComponent).IsAssignableFrom)
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(p => p.GetCustomAttribute<InjectAttribute>() != null)
            .Select(p => p.PropertyType)
            .Where(IsThargaOwned)
            .Where(t => !t.IsGenericParameter && !t.ContainsGenericParameters)
            .Distinct()
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// A Tharga contract, excluding the ones another package or the host owns. Framework and third-party
    /// services are deliberately out of scope — this package does not register those and should not.
    /// </summary>
    private static bool IsThargaOwned(Type type)
    {
        if (type.Namespace?.StartsWith("Tharga", StringComparison.Ordinal) != true) return false;
        if (FromOptionalPackages.Contains(type.Name)) return false;
        if (FromTheHost.Contains(type.Name)) return false;

        return true;
    }

    [Theory]
    [MemberData(nameof(InjectedThargaServices))]
    public void AnInjectedService_Resolves(Type serviceType)
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetService(serviceType);

        Assert.True(resolved != null,
            $"A component injects '{serviceType.Name}', but AddThargaTeamBlazor does not register it. " +
            "A Blazor @inject is required and resolved at render time, so this is not a startup failure — " +
            "it is a page that terminates the circuit when someone opens it. Register it here (TryAdd, so a " +
            "host substituting its own still wins), or, if another package genuinely owns it, add it to the " +
            "exclusion list with the reason.");
    }

    /// <summary>
    /// The scan can silently match nothing — a changed attribute, a moved namespace — and still pass while
    /// reading as "every component checked".
    /// </summary>
    [Fact]
    public void TheGuard_ActuallyCoversSomething()
    {
        var injected = InjectedTypes();

        Assert.NotEmpty(injected);
        Assert.Contains(injected, t => t == typeof(IIconProcessor));
    }

    private sealed class StubJsRuntime : Microsoft.JSInterop.IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object[] args)
            => ValueTask.FromResult(default(TValue));

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object[] args)
            => ValueTask.FromResult(default(TValue));
    }

    /// <summary>A Blazor host supplies this; the package under test does not and should not.</summary>
    private sealed class StubNavigationManager : NavigationManager
    {
        public StubNavigationManager() => Initialize("https://localhost/", "https://localhost/");

        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class StubAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}
