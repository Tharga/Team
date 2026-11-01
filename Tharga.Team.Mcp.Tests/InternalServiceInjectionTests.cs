using System.ComponentModel;
using System.Reflection;
using Tharga.Mcp;
using Tharga.Team;

namespace Tharga.Team.Mcp.Tests;

/// <summary>
/// No MCP provider may take a dependency on an <b>internal service</b> — the deliberately unchecked contract
/// a host implements, which exists so framework code can read team data while constructing the very claims
/// that would authorize the read.
/// </summary>
/// <remarks>
/// The sibling of the component guard in <c>Tharga.Team.Blazor.Tests</c>, kept here because each assembly
/// guards its own first-level surfaces. MCP is the surface where this went wrong before: the <c>team:read</c>
/// scope was registered, documented and granted while being enforced by nothing, and the MCP surface is
/// recorded as having grown a third copy of the rule before anyone noticed.
/// <para>
/// Providers today inject <c>ITeamManagementService</c> / <c>ITeamDirectoryService</c>, whose members all
/// carry <c>[RequireScope]</c>. This fails if that changes.
/// </para>
/// </remarks>
public class InternalServiceInjectionTests
{
    /// <summary>
    /// Unchecked contracts that are not marked <c>EditorBrowsable(Never)</c>, so the attribute scan below cannot find them:
    /// <see cref="IApiKeyAdministrationService"/> reads and writes keys with no scope check and no private-key filtering.
    /// The MCP key listing once injected it and showed every member's private keys to anyone who could reach it; the checked
    /// facet is <see cref="IApiKeyManagementService"/>.
    /// </summary>
    private static readonly Type[] UncheckedByDesign = [typeof(IApiKeyAdministrationService)];

    private static Type[] InternalServices()
        => typeof(ITeamService).Assembly.GetTypes()
            .Where(t => t.IsInterface && t.IsPublic)
            .Where(t => t.GetCustomAttribute<EditorBrowsableAttribute>()?.State == EditorBrowsableState.Never)
            .Concat(UncheckedByDesign)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

    public static TheoryData<Type> Providers()
    {
        var data = new TheoryData<Type>();
        foreach (var type in typeof(TeamResourceProvider).Assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false })
                     .Where(t => typeof(IMcpResourceProvider).IsAssignableFrom(t) || typeof(IMcpToolProvider).IsAssignableFrom(t))
                     .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            data.Add(type);
        }

        return data;
    }

    private static string[] Offenders(Type type)
    {
        var internalServices = InternalServices();

        return type.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .Where(internalServices.Contains)
            .Select(t => t.Name)
            .Distinct()
            .ToArray();
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void AProvider_DoesNotDependOnAnInternalService(Type providerType)
    {
        var offenders = Offenders(providerType);

        Assert.True(
            offenders.Length == 0,
            $"'{providerType.Name}' depends on {string.Join(", ", offenders)}, which is deliberately " +
            "unchecked and bypasses authorization entirely. Inject a gated facet instead — " +
            $"'{nameof(ITeamManagementService)}' and its siblings carry [RequireScope] on every member.");
    }

    /// <summary>Discovery and the internal-service set can each silently become empty, leaving a green test that checks nothing.</summary>
    [Fact]
    public void TheGuard_ActuallyCoversSomething()
    {
        Assert.NotEmpty(Providers());
        Assert.Contains(typeof(ITeamService), InternalServices());
    }

    /// <summary>Proves the guard bites — every real provider passes, so nothing else demonstrates the check works.</summary>
    [Fact]
    public void TheGuard_CatchesAViolation()
    {
        Assert.Equal([nameof(ITeamService)], Offenders(typeof(TakesTheInternalService)));
    }

    [Fact]
    public void TheGuard_CatchesTheUncheckedApiKeyService()
    {
        Assert.Equal([nameof(IApiKeyAdministrationService)], Offenders(typeof(TakesTheUncheckedKeyService)));
    }

    private sealed class TakesTheInternalService
    {
        public TakesTheInternalService(ITeamService teamService) { }
    }

    private sealed class TakesTheUncheckedKeyService
    {
        public TakesTheUncheckedKeyService(IApiKeyAdministrationService keys) { }
    }
}
