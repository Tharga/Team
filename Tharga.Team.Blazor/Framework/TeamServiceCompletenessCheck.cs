using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Reports, at startup, any team-service facet that cannot be resolved, and any storage extension point
/// the host has granted a way to reach without implementing it.
/// </summary>
/// <remarks>
/// The team half of what <see cref="UserServiceCompletenessCheck"/> does for the user half. Splitting
/// <c>ITeamService</c> into facets broke a consuming host's startup twice — once at 3.5.2, again at
/// 3.10.0 — and each time the failure arrived somewhere unhelpful:
/// <list type="bullet">
/// <item>an added interface breaks no signature, so an API diff reads it as a new capability rather than
/// a new obligation;</item>
/// <item><c>ValidateOnBuild</c> is on by default only in Development, so a host whose integration tests
/// boot elsewhere stays green while the application cannot start;</item>
/// <item>a Blazor <c>@inject</c> resolves at <b>render</b> time, so a missing facet surfaces as a 500 on
/// whichever page nobody opened until production.</item>
/// </list>
/// Saying so once at startup, naming the interface, is the cheapest place to find out.
/// <para>
/// <b>Logs an error rather than throwing, by default</b> — the same trade the user-side check documents:
/// turning a pre-existing gap into a boot failure after a routine upgrade is a worse outcome than making
/// it impossible to miss in the log. Set <c>ThrowOnIncompleteTeamService</c> for the strict reading.
/// </para>
/// </remarks>
internal sealed class TeamServiceCompletenessCheck(
    IServiceProvider serviceProvider,
    Type teamServiceType,
    bool throwOnIncomplete,
    ILogger<TeamServiceCompletenessCheck> logger = null) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (teamServiceType == null) return Task.CompletedTask;

        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;

        Report(DescribeUnreachableGaps(sp));

        var missing = TeamServiceFacets.All.Where(f => sp.GetService(f) == null).ToArray();
        if (missing.Length == 0) return Task.CompletedTask;

        var detail = string.Join(Environment.NewLine, missing.Select(m => $"  - {m.Name}"));
        var inferred = TeamMemberTypeResolver.Resolve(teamServiceType);

        // Naming the cause as well as the symptom: the reader can act on the first, and can only go
        // reading the toolkit's source on the second.
        var cause = inferred == null
            ? $"No team member type could be determined from '{teamServiceType.Name}'. It does not derive " +
              $"from a generic base carrying one (such as TeamServiceRepositoryBase<TEntity, TMember>), so " +
              $"name it explicitly: RegisterTeamService<{teamServiceType.Name}, TUserService, TMember>()."
            : $"A member type ('{inferred.Name}') was determined, so these were most likely replaced or " +
              $"removed by the host's own registration.";

        var message =
            $"{missing.Length} team service interface(s) cannot be resolved. Anything injecting one fails " +
            $"when it is first used, which for a Blazor component is at render time:{Environment.NewLine}" +
            $"{detail}{Environment.NewLine}{cause}";

        Report(message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Storage extension points the host has not implemented but has granted a way to reach — today, the
    /// cross-team listing behind <see cref="SystemTeamScopes.Read"/>. Null when there is nothing to say.
    /// </summary>
    private string DescribeUnreachableGaps(IServiceProvider sp)
    {
        var gaps = TeamServiceCompleteness.Find(teamServiceType, IsTeamsReadReachable(sp));
        if (gaps.Count == 0) return null;

        var detail = string.Join(Environment.NewLine, gaps.Select(g => $"  - {g}"));
        return
            $"'{teamServiceType.Name}' derives from TeamServiceBase without implementing what the " +
            $"'{SystemTeamScopes.Read}' system scope needs:{Environment.NewLine}{detail}{Environment.NewLine}" +
            $"The scope switches team pages into cross-team oversight mode, which lists every team from the " +
            $"service. Override GetAllTeamsInternalAsync to enumerate your store, or stop granting the scope. " +
            $"Left as it is, a caller holding it cannot load a team page.";
    }

    /// <summary>
    /// Whether any caller can end up holding <see cref="SystemTeamScopes.Read"/>: a system role maps it
    /// (which includes <c>Consent.GrantTeamsRead</c>, applied through the same registry), or it is
    /// registered as a system scope, which is what a system API key is issued from.
    /// </summary>
    private static bool IsTeamsReadReachable(IServiceProvider sp)
    {
        var byRole = (sp.GetService<ISystemRoleRegistry>() as SystemRoleRegistry)?.All.Values
            .Any(scopes => scopes.Contains(SystemTeamScopes.Read, StringComparer.Ordinal)) ?? false;
        var byKey = sp.GetService<ISystemScopeRegistry>()?.All
            .Any(d => d.Name == SystemTeamScopes.Read) ?? false;

        return byRole || byKey;
    }

    private void Report(string message)
    {
        if (message == null) return;
        if (throwOnIncomplete) throw new InvalidOperationException(message);

        logger?.LogError("{Message}", message);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
