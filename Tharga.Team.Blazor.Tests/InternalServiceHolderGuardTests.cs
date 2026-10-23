using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// What is not a component may hold an <b>internal service</b> only where it is written down here, may call
/// only the <see cref="ITeamService"/> members written down for it, and may not fetch one from a service
/// provider.
/// </summary>
/// <remarks>
/// <b>The gap <see cref="InternalServiceInjectionTests"/> leaves.</b> That guard reads components. The selector
/// state and the simulation state are plain classes, so both inject the unchecked contract on purpose and sit
/// outside it, and being outside it has already cost one production defect: <c>AccessSimulationState</c> read
/// member names through a gated call, and every team Owner opening <b>View as another user…</b> was refused.
/// The same goes for a component that fetches the contract with <c>GetRequiredService</c> instead of injecting
/// it, which no constructor or <c>[Inject]</c> scan sees.
/// <para>
/// <b>The member pin is the equivalent of <see cref="GatedUserReadGuardTests"/>.</b> <see cref="ITeamService"/>
/// members carry no <c>[RequireScope]</c>, so there is nothing to discover by reflection; the decision is
/// instead which members each holder is allowed to reach. A new call from a holder fails here until someone
/// writes down why it is safe. A reference handed on to another class, as the simulation state does to
/// <c>TeamGrantResolver</c>, is not a call and is not seen.
/// </para>
/// <para>
/// <b>Do not add an entry to silence a failure.</b> An entry is a claim that the surface decides who may see
/// the answer some other way, and the justification belongs on the holder's own XML docs.
/// </para>
/// </remarks>
public class InternalServiceHolderGuardTests
{
    private const string RegistrationFile = "Framework/ThargaBlazorRegistration.cs";

    private static readonly Regex SourceFile = new(@"\.(razor|cs)$", RegexOptions.Compiled);
    private static readonly Regex TeamServiceCall = new(@"\b_?teamService\.(?<member>\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// The non-component classes holding an internal service, with the <see cref="ITeamService"/> members each
    /// may call directly.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> Holders = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["TeamStateService"] =
        [
            nameof(ITeamService.TeamsListChangedEvent),
            nameof(ITeamService.SelectTeamEvent),
            nameof(ITeamService.GetTeamsAsync),
            nameof(ITeamService.GetAllTeamsAsync),
            nameof(ITeamService.GetTeamByKeyAsync),
            nameof(ITeamService.CreateTeamAsync),
            nameof(ITeamService.SetMemberLastSeenAsync)
        ],
        ["AccessSimulationState"] = [nameof(ITeamService.GetMembersAsync)],
        ["TeamClaimsAuthenticationStateProvider"] = [nameof(ITeamService.GetTeamMemberAsync)],
        ["TeamMembershipClaimsBuilder"] = []
    };

    private static Type[] InternalServices()
        => typeof(ITeamService).Assembly.GetTypes()
            .Where(t => t.IsInterface && t.IsPublic)
            .Where(t => t.GetCustomAttribute<EditorBrowsableAttribute>()?.State == EditorBrowsableState.Never)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

    private static Regex ProviderResolution()
        => new($@"Get\w*Service\s*(<\s*({string.Join("|", InternalServices().Select(t => Regex.Escape(t.Name)))})\s*>|\(\s*typeof\(\s*({string.Join("|", InternalServices().Select(t => Regex.Escape(t.Name)))})\s*\))");

    private static IEnumerable<Type> Dependencies(Type type)
    {
        foreach (var parameter in type.GetConstructors().SelectMany(c => c.GetParameters()))
            yield return parameter.ParameterType;

        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        foreach (var property in type.GetProperties(all))
        {
            if (property.GetCustomAttribute<InjectAttribute>() != null)
                yield return property.PropertyType;
        }
    }

    private static string[] Held(Type type)
    {
        var internalServices = InternalServices();

        return Dependencies(type)
            .Where(internalServices.Contains)
            .Select(t => t.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] FoundHolders()
        => typeof(ThargaBlazorOptions).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && !t.IsDefined(typeof(CompilerGeneratedAttribute)))
            .Where(t => !typeof(Microsoft.AspNetCore.Components.IComponent).IsAssignableFrom(t))
            .Where(t => Held(t).Length > 0)
            .Select(t => t.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

    private static DirectoryInfo SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Blazor"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return new DirectoryInfo(Path.Combine(dir.FullName, "Tharga.Team.Blazor"));
    }

    private static (string File, string Text)[] Sources()
    {
        var root = SourceRoot();

        return [.. root.GetFiles("*", SearchOption.AllDirectories)
            .Where(f => SourceFile.IsMatch(f.Name))
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => (Path.GetRelativePath(root.FullName, f.FullName).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText(f.FullName)))];
    }

    private static string[] CallsOnTeamService(string text)
        => [.. TeamServiceCall.Matches(text)
            .Select(m => m.Groups["member"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)];

    public static TheoryData<string> HolderNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Holders.Keys.OrderBy(x => x, StringComparer.Ordinal)) data.Add(name);
        return data;
    }

    [Fact]
    public void ANonComponent_HoldsAnInternalService_OnlyWhereItIsWrittenDown()
    {
        var unlisted = FoundHolders().Except(Holders.Keys, StringComparer.Ordinal).ToArray();

        Assert.True(
            unlisted.Length == 0,
            $"These classes are not components, so the component guard does not read them, and they take an internal " +
            $"service, which is unchecked: {string.Join(", ", unlisted)}. Inject a gated facet instead. If the read " +
            "genuinely names no team, say why in the class's XML docs and add it to the holders here.");
    }

    [Fact]
    public void EveryHolderWrittenDown_StillHoldsAnInternalService()
    {
        var stale = Holders.Keys.Except(FoundHolders(), StringComparer.Ordinal).ToArray();

        Assert.True(
            stale.Length == 0,
            $"These no longer hold an internal service, so the entry excuses nothing and should be deleted: {string.Join(", ", stale)}.");
    }

    [Theory]
    [MemberData(nameof(HolderNames))]
    public void AHolder_CallsOnlyTheTeamServiceMembersItIsAllowed(string holder)
    {
        var text = Sources().Single(x => x.File.EndsWith($"/{holder}.cs", StringComparison.Ordinal)).Text;
        var calls = CallsOnTeamService(text);
        var allowed = Holders[holder];

        var unexpected = calls.Except(allowed, StringComparer.Ordinal).ToArray();
        var unused = allowed.Except(calls, StringComparer.Ordinal).ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"'{holder}' calls {string.Join(", ", unexpected)} on the internal ITeamService, which it is not written down " +
            "as allowed to. Each of those is unchecked; prefer a gated facet, or write down why the answer cannot reach a " +
            "caller who should not have it.");
        Assert.True(
            unused.Length == 0,
            $"'{holder}' no longer calls {string.Join(", ", unused)}, so the allowance excuses nothing and should be deleted.");
    }

    [Fact]
    public void NothingFetchesAnInternalService_FromAServiceProvider_ExceptTheRegistration()
    {
        var pattern = ProviderResolution();

        var offenders = Sources()
            .Where(x => pattern.IsMatch(x.Text))
            .Select(x => x.File)
            .Where(file => file != RegistrationFile)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"These resolve an internal service from a service provider, which no constructor or [Inject] scan sees and " +
            $"which bypasses authorization the same way injecting it does: {string.Join(", ", offenders)}.");
    }

    [Fact]
    public void TheRegistrationExemption_StillResolvesAnInternalService()
    {
        var registration = Sources().Single(x => x.File == RegistrationFile);

        Assert.Matches(ProviderResolution(), registration.Text);
    }

    /// <summary>
    /// Discovery, the internal-service set and the source scan can each silently become empty, leaving a green
    /// test that checks nothing.
    /// </summary>
    [Fact]
    public void TheGuard_ActuallyCoversSomething()
    {
        Assert.Contains(typeof(ITeamService), InternalServices());
        Assert.True(FoundHolders().Length >= Holders.Count, "The scan finds fewer holders than are written down.");
        Assert.True(Sources().Length > 50, "The source scan is not reaching the project.");
        Assert.True(
            CallsOnTeamService(Sources().Single(x => x.File.EndsWith("/TeamStateService.cs", StringComparison.Ordinal)).Text).Length >= 5,
            "The member scan stopped matching how the selector state calls the service.");
    }

    /// <summary>
    /// Proves each of the three checks bites. Every real surface passes, so without this a scan matching the wrong
    /// thing stays green while reading as protection.
    /// </summary>
    [Fact]
    public void TheGuard_CatchesAViolation()
    {
        Assert.Equal([nameof(ITeamService)], Held(typeof(TakesTheInternalServiceOnItsConstructor)));
        Assert.Equal([nameof(ITeamService)], Held(typeof(InjectsTheInternalService)));
        Assert.Empty(Held(typeof(TakesAGatedFacet)));

        Assert.Equal(["RemoveMemberAsync"], CallsOnTeamService("await _teamService.RemoveMemberAsync(teamKey, userKey);"));
        Assert.Equal(["GetMembersAsync"], CallsOnTeamService("await foreach (var m in teamService.GetMembersAsync(key)) { }"));

        var pattern = ProviderResolution();
        Assert.Matches(pattern, "var service = services.GetRequiredService<ITeamService>();");
        Assert.Matches(pattern, "provider.GetService(typeof(ITeamService))");
        Assert.DoesNotMatch(pattern, "ServiceProvider.GetService<ITeamManagementService>()");
    }

    private sealed class TakesTheInternalServiceOnItsConstructor
    {
        public TakesTheInternalServiceOnItsConstructor(ITeamService teamService) { }
    }

    private sealed class InjectsTheInternalService
    {
        [Inject] private ITeamService TeamService { get; set; }
    }

    private sealed class TakesAGatedFacet
    {
        public TakesAGatedFacet(ITeamManagementService teamManagementService) { }
    }
}
