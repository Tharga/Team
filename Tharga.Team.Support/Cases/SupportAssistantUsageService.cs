using Tharga.Team.Service;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Reads the assistant's usage from the store; nothing where none is registered.
/// </summary>
/// <remarks>Authorization is the decorator's, as for every other support service.</remarks>
internal sealed class SupportAssistantUsageService(ISupportAssistantUsageStore store = null) : ISupportAssistantUsageService
{
    public async Task<SupportAssistantUsageTotal[]> GetUsageAsync(DateTime from, DateTime to, string teamKey = null, CancellationToken cancellationToken = default)
        => store == null ? [] : await store.GetTotalsAsync(from, to, teamKey, cancellationToken);
}

/// <summary>
/// The one place reading the assistant's usage is authorized: it requires <see cref="SystemSupportScopes.UsageRead"/>.
/// </summary>
/// <remarks>
/// A system scope, not a team one, also for a single team: the read is for whoever runs or bills the product, and a
/// team's administrators hold nothing that should show them what it cost.
/// </remarks>
internal sealed class AuthorizationSupportAssistantUsageServiceDecorator(
    ISupportAssistantUsageService inner,
    TeamAuthorizer authorizer) : ISupportAssistantUsageService
{
    public async Task<SupportAssistantUsageTotal[]> GetUsageAsync(DateTime from, DateTime to, string teamKey = null, CancellationToken cancellationToken = default)
    {
        if (!await authorizer.HasSystemScopeAsync(SystemSupportScopes.UsageRead))
            throw new UnauthorizedAccessException($"Reading the support assistant's usage requires the '{SystemSupportScopes.UsageRead}' system scope.");

        return await inner.GetUsageAsync(from, to, teamKey, cancellationToken);
    }
}

/// <summary>
/// Removes a team's assistant usage records when the team is purged.
/// </summary>
/// <remarks>Each record names who asked; a purged team's records left behind keep its members' identities.</remarks>
internal sealed class SupportAssistantUsagePurgeParticipant(ISupportAssistantUsageStore store) : ITeamPurgeParticipant
{
    public string Name => "support assistant usage";

    public async Task<int> PurgeTeamDataAsync(string teamKey, CancellationToken cancellationToken = default)
        => (int)Math.Min(int.MaxValue, await store.DeleteForTeamAsync(teamKey, cancellationToken));
}

/// <summary>Stands in for the usage participant where no usage store is registered: there is nothing to purge.</summary>
internal sealed class NoUsageToPurge : ITeamPurgeParticipant
{
    public static readonly NoUsageToPurge Instance = new();

    public string Name => "support assistant usage";

    public Task<int> PurgeTeamDataAsync(string teamKey, CancellationToken cancellationToken = default) => Task.FromResult(0);
}
