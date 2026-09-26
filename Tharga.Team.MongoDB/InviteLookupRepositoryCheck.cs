using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tharga.Team.MongoDB;

/// <summary>
/// Whether a team repository leaves a member at the interface's default implementation.
/// </summary>
internal static class TeamRepositoryCompleteness
{
    /// <summary>
    /// True when <paramref name="implementationType"/> does not implement <paramref name="method"/> of
    /// <paramref name="interfaceType"/> itself, so calls land on the interface's default body.
    /// </summary>
    public static bool UsesDefault(Type implementationType, Type interfaceType, string method)
    {
        if (implementationType == null || !interfaceType.IsAssignableFrom(implementationType)) return false;

        var map = implementationType.GetInterfaceMap(interfaceType);
        var index = Array.FindIndex(map.InterfaceMethods, m => m.Name == method);

        return index >= 0 && map.TargetMethods[index].DeclaringType == interfaceType;
    }
}

/// <summary>
/// Reports, at startup, a team repository that cannot look an invitation up by its code — the store half of
/// Tharga/Team#286.
/// </summary>
/// <remarks>
/// <see cref="TeamServiceRepositoryBase{TTeamEntity,TMember}"/> answers the service's invitation lookup by
/// calling <see cref="ITeamRepository{TTeamEntity,TMember}.GetByInviteKeyAsync"/>, whose default returns null.
/// So a host that replaced the built-in repository with its own, and did not implement that member, mints
/// invitation links that never resolve — and the service-side check cannot see it, because the service did
/// override its half.
/// <para>
/// <b>Logs an error rather than throwing</b>, and guards the resolve: a diagnostic must never be the reason
/// an application fails to boot.
/// </para>
/// </remarks>
internal sealed class InviteLookupRepositoryCheck<TTeamEntity, TMember>(
    IServiceProvider serviceProvider,
    ILogger<InviteLookupRepositoryCheck<TTeamEntity, TMember>> logger = null) : IHostedService
    where TTeamEntity : TeamEntityBase<TMember>
    where TMember : TeamMemberBase
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetService<ITeamRepository<TTeamEntity, TMember>>();
            if (repository == null) return Task.CompletedTask;

            var method = nameof(ITeamRepository<TTeamEntity, TMember>.GetByInviteKeyAsync);
            if (!TeamRepositoryCompleteness.UsesDefault(repository.GetType(), typeof(ITeamRepository<TTeamEntity, TMember>), method))
                return Task.CompletedTask;

            logger?.LogError(
                "'{Repository}' does not implement {Method}. Every invitation link carries only its code, and " +
                "without this lookup none of them resolves - the recipient is told the link is no longer valid. " +
                "Implement it to return the single live team holding an outstanding invitation with that code.",
                repository.GetType().Name, method);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not check whether the team repository can look up invitations. This is a diagnostic only and has not affected startup.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
