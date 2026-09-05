using System.Reflection;
using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// One place decides what a caller may do in a team.
/// </summary>
/// <remarks>
/// <b>This is a convention nobody can hold in their head, so it is asserted instead.</b> The toolkit has
/// paid for a second copy twice: once when <c>team:read</c> was registered, documented, granted and checked
/// by nothing, and again in Tharga/Team#248, where the gate on <c>ITeamManagementService</c>'s reads
/// recomputed scopes from the member row and so could not see consent, tenant roles, or suspension.
/// <para>
/// Both times the second copy was correct when written and drifted afterwards. A review cannot catch that;
/// the drift happens in the copy nobody is looking at.
/// </para>
/// </remarks>
public class TeamGrantSingleEnforcementTests
{
    private const string ResolverName = "TeamGrantResolver";

    /// <summary>
    /// Types allowed to compute effective scopes besides the resolver, each with the reason it is not a
    /// second copy of the grant rule.
    /// </summary>
    /// <remarks>
    /// <b>The list is the point.</b> Adding an entry is a deliberate act with a reason attached, which is
    /// what distinguishes a considered exception from the convenient re-derivation this test exists to
    /// catch. All three current entries concern something other than "what may this <i>user</i> do in this
    /// team", which is the only question the resolver owns.
    /// </remarks>
    private static readonly Dictionary<string, string> Allowed = new()
    {
        ["TenantRoleService"] =
            "The scope-composition primitive itself — it is what the resolver calls, not a rival to it.",
        ["TeamContextResolver"] =
            "The API-key consent path. A key holds no roles, so consent for a key is the team's level " +
            "rather than a role match; that is a different rule, documented on the type, not a copy of this one.",
        ["ApiKeyAuthenticationHandler"] =
            "Builds claims for an API key, whose access level and roles come from the key record rather " +
            "than from membership or consent."
    };

    private static readonly string[] ScopeComputingMembers =
    [
        nameof(IScopeRegistry.GetEffectiveScopes),
        nameof(ITenantRoleService.GetEffectiveScopesAsync)
    ];

    private static IEnumerable<Assembly> ProductionAssemblies =>
    [
        typeof(TeamManagementService<>).Assembly,
        typeof(TeamAuthorizer).Assembly
    ];

    /// <summary>
    /// Every type that turns an access level, tenant roles and overrides into effective scopes, found by
    /// reading IL rather than by grepping — a rename cannot slip past it.
    /// </summary>
    private static IEnumerable<(string Assembly, string Type, string Method)> ScopeComputingCallSites()
    {
        foreach (var assembly in ProductionAssemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (!CallsAScopeComputingMember(method)) continue;

                    yield return (assembly.GetName().Name, DeclaringTypeName(type), method.Name);
                }
            }
        }
    }

    /// <summary>
    /// The compiler-generated state machine for an async method is a nested type, so report the method's
    /// owner rather than <c>&lt;ResolveAsync&gt;d__7</c>, which names nothing a reader can act on.
    /// </summary>
    private static string DeclaringTypeName(Type type)
    {
        var current = type;
        while (current.IsNested && current.Name.StartsWith('<')) current = current.DeclaringType;

        return current.Name.Split('`')[0];
    }

    private static bool CallsAScopeComputingMember(MethodInfo method)
    {
        var body = method.GetMethodBody();
        if (body == null) return false;

        var il = body.GetILAsByteArray();
        if (il == null) return false;

        var module = method.Module;

        for (var i = 0; i < il.Length - 4; i++)
        {
            // call (0x28) and callvirt (0x6F) are both four-byte-operand metadata token calls.
            if (il[i] != 0x28 && il[i] != 0x6F) continue;

            var token = BitConverter.ToInt32(il, i + 1);

            string name;
            try
            {
                name = module.ResolveMethod(token, method.DeclaringType?.GetGenericArguments(), method.GetGenericArguments())?.Name;
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (name != null && ScopeComputingMembers.Contains(name)) return true;
        }

        return false;
    }

    /// <summary>
    /// The scan has to find the resolver itself, or it is matching nothing and would pass forever while
    /// reading as "everything checked".
    /// </summary>
    [Fact]
    public void TheScan_FindsTheResolver()
    {
        var sites = ScopeComputingCallSites().ToArray();

        Assert.Contains(sites, s => s.Type == ResolverName);
    }

    /// <summary>
    /// Nothing but the resolver and the listed exceptions computes effective team scopes. A new call site
    /// is not necessarily wrong — but it is a second copy of the rule, so it has to be argued into
    /// <see cref="Allowed"/> rather than merely written.
    /// </summary>
    [Fact]
    public void OnlyTheResolver_ComputesEffectiveTeamScopes()
    {
        var offenders = ScopeComputingCallSites()
            .Where(s => s.Type != ResolverName && !Allowed.ContainsKey(s.Type))
            .Select(s => $"{s.Assembly}.{s.Type}.{s.Method}")
            .Distinct()
            .Order()
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"Effective team scopes must be computed only by {ResolverName}, so consent, tenant roles and " +
            $"suspension are decided once (Tharga/Team#248). Also computing them: " +
            $"{string.Join(", ", offenders)}. If one of these is genuinely a different question — an API " +
            $"key rather than a user, say — add it to {nameof(Allowed)} with the reason.");
    }

    /// <summary>
    /// An allowlist entry that no longer matches anything is a stale exemption, and a stale exemption is
    /// how a real second copy gets waved through later under a name someone recognises.
    /// </summary>
    [Fact]
    public void EveryAllowedException_StillComputesScopes()
    {
        var computing = ScopeComputingCallSites().Select(s => s.Type).Distinct().ToHashSet();

        var stale = Allowed.Keys.Where(t => !computing.Contains(t)).Order().ToArray();

        Assert.True(stale.Length == 0,
            $"These types are exempted from the single-enforcement rule but no longer compute scopes: " +
            $"{string.Join(", ", stale)}. Remove them from {nameof(Allowed)}.");
    }
}
