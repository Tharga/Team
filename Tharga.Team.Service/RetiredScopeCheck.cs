using Microsoft.Extensions.Hosting;

namespace Tharga.Team.Service;

/// <summary>
/// Fails startup when a host still registers a scope name the toolkit has retired, naming its replacement.
/// </summary>
/// <remarks>
/// <b>A renamed scope fails silently, which is why this exists.</b> Nothing about
/// <c>teams:assign-owner</c> → <see cref="SystemTeamScopes.SetOwner"/> is visible to the compiler: a host
/// maps scope names as strings in <c>ConfigureSystemRoles</c> or on a system API key, so the old name keeps
/// registering happily and simply stops authorizing anything. The first sign would be an operator being
/// refused a capability they believe they were granted — at the point of use, in production, with nothing
/// in the logs explaining why.
/// <para>
/// Throwing at boot converts that into one line naming both strings. This is the same reasoning as
/// <c>TeamServiceCompletenessCheck</c>: the failure a host cannot diagnose is worth more noise at startup
/// than a quiet wrong answer later.
/// </para>
/// <para>
/// <b>Delete this once 4.0 ships.</b> It exists to carry hosts across one rename, not forever; the entry
/// for it is in the backlog rather than left to memory.
/// </para>
/// </remarks>
internal sealed class RetiredScopeCheck(ISystemScopeRegistry systemScopes) : IHostedService
{
    /// <summary>
    /// Retired names, held here rather than on <see cref="SystemTeamScopes"/> so that nothing advertises a
    /// scope string that authorizes nothing.
    /// </summary>
    private const string RetiredAssignOwner = "teams:assign-owner";

    private static readonly (string Retired, string Replacement, string Why)[] Renames =
    [
        (RetiredAssignOwner, SystemTeamScopes.SetOwner,
            "it now authorizes making any member the sole owner of any team, not only repairing an ownerless one")
    ];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var (retired, replacement, why) in Renames)
        {
            if (systemScopes.All.All(s => s.Name != retired)) continue;

            throw new InvalidOperationException(
                $"The system scope '{retired}' has been retired and authorizes nothing. Register " +
                $"'{replacement}' instead, and update any system role mapping or API key that grants the " +
                $"old name — {why}. Left as it is, holders would be refused with no indication why.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
