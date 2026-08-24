using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tharga.Team.MongoDB;

/// <summary>
/// Reports, at startup, any stored team member carrying no access level — and therefore being treated as
/// <see cref="AccessLevel.Owner"/>.
/// </summary>
/// <remarks>
/// <b>Warns rather than throwing, and unconditionally so.</b> The incomplete-service checks offer a
/// <c>ThrowOn…</c> switch because they report a wiring mistake a developer can fix before deploying. This
/// reports *data*, which no code change corrects and which a host may have carried for years. Refusing to
/// start would turn a silent pre-existing grant into an outage, on a version the host upgraded to for
/// unrelated reasons.
/// <para>
/// <b>The whole query is guarded.</b> A diagnostic must never be the reason an application fails to boot, so
/// an unreachable or unreadable store is logged and swallowed rather than propagated.
/// </para>
/// <para>
/// A healthy deployment costs exactly one count and never materializes a document. Only when the count is
/// non-zero does it read teams, and then at most <see cref="MaxNamedTeams"/> of them — enough to act on,
/// bounded so a large affected tenant cannot turn startup into a full-collection load.
/// </para>
/// </remarks>
internal sealed class AccessLevelCompletenessCheck<TTeamEntity, TMember>(
    IServiceProvider serviceProvider,
    ILogger<AccessLevelCompletenessCheck<TTeamEntity, TMember>> logger = null) : IHostedService
    where TTeamEntity : TeamEntityBase<TMember>
    where TMember : TeamMemberBase
{
    private const int MaxNamedTeams = 10;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var collection = scope.ServiceProvider.GetService<ITeamRepositoryCollection<TTeamEntity, TMember>>();
            if (collection == null) return;

            var filter = AccessLevelCompleteness.MembersWithNoAccessLevel<TTeamEntity, TMember>();

            var affected = await collection.CountAsync(filter);
            if (affected == 0) return;

            var keys = new List<string>();
            await foreach (var team in collection.GetAsync(filter).WithCancellation(cancellationToken))
            {
                keys.Add(team.Key);
                if (keys.Count >= MaxNamedTeams) break;
            }

            var named = string.Join(", ", keys.Select(k => $"'{k}'"));
            var remainder = affected > keys.Count ? $" (and {affected - keys.Count} more)" : string.Empty;

            logger?.LogWarning(
                "{Count} team(s) contain members with no stored AccessLevel: {Teams}{Remainder}. " +
                "Those members are being treated as Owner, because Owner is the enum's zero value and an " +
                "absent field cannot be told from a stored one after loading. Nothing has changed - they " +
                "were already Owner - but a future major release will refuse them instead of granting " +
                "Owner, so set an explicit level on each. See the implementation guide, 'Members with no " +
                "access level', for the query that lists and fixes them.",
                affected, named, remainder);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not check whether any stored team member is missing an AccessLevel. This is a diagnostic only and has not affected startup.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
