using System.Reflection;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The support components use only the public surface a host could use.
/// </summary>
/// <remarks>
/// <b>This is the whole reason shipping a component is acceptable.</b> The standing rule is that a host must
/// always be able to build its own instead — so a shipped component is a demonstration that the surface
/// suffices, not a privileged path. If one of these needed something a consumer cannot reach, the surface
/// would be incomplete and the component would be hiding it.
/// <para>
/// Asserted rather than assumed, because it decays silently: reaching for a store or an internal service
/// compiles perfectly and nothing else complains.
/// </para>
/// </remarks>
public class SupportComponentSurfaceTests
{
    /// <summary>
    /// The contract namespaces, matched <b>exactly</b>.
    /// </summary>
    /// <remarks>
    /// <b>Exactly, not by prefix, and that is the point of splitting these two lists.</b> Permitting
    /// <c>Tharga.Team</c> as a prefix would also permit <c>Tharga.Team.Service</c> and
    /// <c>Tharga.Team.MongoDB</c> — the internal services and the storage adapter — which is precisely what
    /// this test exists to keep out of a component. A guard that admits everything under the namespace it
    /// meant to admit one level of is a guard that passes for the wrong reason.
    /// </remarks>
    private static readonly string[] PermittedNamespaces =
    [
        "Tharga.Team",
        "Tharga.Team.Support.Cases"
    ];

    /// <summary>
    /// The UI and platform namespaces, matched by prefix — a component is expected to reach across all of
    /// these freely.
    /// </summary>
    private static readonly string[] PermittedNamespacePrefixes =
    [
        "Tharga.Team.Blazor",
        "Tharga.Blazor",
        "Radzen",
        "Microsoft",
        "System"
    ];

    /// <summary>
    /// What a host cannot resolve, or must not: the persistence port, and anything marked as the contract a
    /// host implements rather than consumes.
    /// </summary>
    private static readonly Type[] Forbidden =
    [
        typeof(ISupportCaseStore),
        typeof(ISupportEventLedger)
    ];

    private static Type[] SupportComponents() =>
        typeof(SupportCasesView).Assembly.GetTypes()
            .Where(t => t.Namespace == "Tharga.Team.Blazor.Features.Support")
            .Where(t => !t.IsNested)
            .ToArray();

    /// <summary>The self-check: an empty scan would satisfy every assertion below.</summary>
    [Fact]
    public void TheScanFindsTheComponents()
    {
        var components = SupportComponents();

        Assert.NotEmpty(components);
        Assert.Contains(components, t => t == typeof(SupportCasesView));
    }

    [Fact]
    public void NoSupportComponent_DependsOnAStoreOrAnInternalService()
    {
        var offenders = new List<string>();

        foreach (var component in SupportComponents())
        {
            foreach (var dependency in InjectedTypes(component))
            {
                if (Forbidden.Contains(dependency)) offenders.Add($"{component.Name} <- {dependency.Name}");
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The detector's self-check: it has to recognise a forbidden type, or "no offenders" only means it never
    /// fires.
    /// </summary>
    [Fact]
    public void TheDetector_RecognisesAForbiddenDependency()
    {
        Assert.Contains(typeof(ISupportCaseStore), Forbidden);
        Assert.DoesNotContain(typeof(ISupportCaseService), Forbidden);
    }

    /// <summary>
    /// The namespace rule must reject an internal service, or the exact-match distinction above achieves
    /// nothing.
    /// </summary>
    [Fact]
    public void TheNamespaceRule_RejectsAnInternalService()
    {
        var internalService = typeof(Tharga.Team.Service.TeamAuthorizer).Namespace;

        Assert.DoesNotContain(internalService, PermittedNamespaces);
        Assert.DoesNotContain(internalService, PermittedNamespacePrefixes);
        Assert.False(PermittedNamespacePrefixes.Any(p => internalService!.StartsWith(p, StringComparison.Ordinal)));
    }

    [Fact]
    public void EverySupportComponentDependency_ComesFromAPermittedNamespace()
    {
        var offenders = new List<string>();

        foreach (var component in SupportComponents())
        {
            foreach (var dependency in InjectedTypes(component))
            {
                var ns = dependency.Namespace ?? string.Empty;

                var permitted = PermittedNamespaces.Contains(ns)
                                || PermittedNamespacePrefixes.Any(p => ns.StartsWith(p, StringComparison.Ordinal));

                if (!permitted) offenders.Add($"{component.Name} <- {dependency.FullName}");
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// A Blazor component takes its dependencies as <c>[Inject]</c> properties rather than constructor
    /// parameters, so that is what has to be walked. Reading constructors would examine nothing and pass.
    /// </summary>
    private static IEnumerable<Type> InjectedTypes(Type component)
        => component
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.GetCustomAttributes().Any(a => a.GetType().Name == "InjectAttribute"))
            .Select(p => p.PropertyType);
}
