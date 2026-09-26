using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
/// Reports, at startup, a team repository that leaves an invitation member at its default.
/// </summary>
/// <remarks>
/// <see cref="TeamServiceRepositoryBase{TTeamEntity,TMember}"/> forwards the service's invitation seams to the
/// repository, so the service-side check cannot see a gap here: the service did override its half. Two members:
/// <list type="bullet">
/// <item><see cref="ITeamRepository{TTeamEntity,TMember}.GetByInviteKeyAsync"/> returns null by default, so
/// every link the toolkit mints fails to resolve (the store half of Tharga/Team#286). Always reported.</item>
/// <item><see cref="ITeamRepository{TTeamEntity,TMember}.SetInvitationExpiryAsync"/> throws by default, so
/// re-inviting cannot renew an invitation. Reported only with <see cref="InvitationOptions.Lifetime"/> set,
/// since without one nothing extends an invitation on its own.</item>
/// </list>
/// <para>
/// <b>Logs an error rather than throwing</b>, and guards the resolve: a diagnostic must never be the reason
/// an application fails to boot.
/// </para>
/// </remarks>
internal sealed class InvitationRepositoryCheck<TTeamEntity, TMember>(
    IServiceProvider serviceProvider,
    ILogger<InvitationRepositoryCheck<TTeamEntity, TMember>> logger = null,
    IOptions<InvitationOptions> invitationOptions = null) : IHostedService
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

            var repositoryType = repository.GetType();
            var interfaceType = typeof(ITeamRepository<TTeamEntity, TMember>);

            var lookup = nameof(ITeamRepository<TTeamEntity, TMember>.GetByInviteKeyAsync);
            if (TeamRepositoryCompleteness.UsesDefault(repositoryType, interfaceType, lookup))
            {
                logger?.LogError(
                    "'{Repository}' does not implement {Method}. Every invitation link carries only its code, and " +
                    "without this lookup none of them resolves - the recipient is told the link is no longer valid. " +
                    "Implement it to return the single live team holding an outstanding invitation with that code.",
                    repositoryType.Name, lookup);
            }

            var expiry = nameof(ITeamRepository<TTeamEntity, TMember>.SetInvitationExpiryAsync);
            if (invitationOptions?.Value?.Lifetime != null && TeamRepositoryCompleteness.UsesDefault(repositoryType, interfaceType, expiry))
            {
                logger?.LogError(
                    "'{Repository}' does not implement {Method}, and invitations expire after {Lifetime}. Re-inviting " +
                    "someone renews their invitation by moving its expiry, so without this it fails and the invitee " +
                    "is left holding an expired link. Implement it to set the expiry of the matching invitation.",
                    repositoryType.Name, expiry, invitationOptions.Value.Lifetime);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not check whether the team repository implements its invitation members. This is a diagnostic only and has not affected startup.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
