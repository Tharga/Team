using System.Diagnostics;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Support.Cases;

/// <summary>Gives a team a spend limit of its own, or takes it away, and says which limit holds.</summary>
/// <remarks>Authorization is the decorator's.</remarks>
internal sealed class SupportAssistantLimitService(
    ISupportAssistantLimitStore store,
    SupportAssistantAllowanceOptions options,
    TimeProvider timeProvider,
    TeamAuthorizer authorizer,
    IPlatformAiCatalogueStore catalogue = null) : ISupportAssistantLimitService
{
    public async Task<SupportAssistantLimit> GetLimitAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        RequireTeam(teamKey);
        return await DescribeAsync(teamKey, await store.GetAsync(teamKey, cancellationToken), cancellationToken);
    }

    public async Task<SupportAssistantLimit> SetLimitAsync(SupportAssistantLimitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireTeam(request.TeamKey);
        if (request.Limit < 0) throw new ArgumentOutOfRangeException(nameof(request), "A limit cannot be negative.");

        var stored = new StoredSupportAssistantLimit
        {
            TeamKey = request.TeamKey,
            Limit = request.Limit,
            ChangedAt = timeProvider.GetUtcNow().UtcDateTime,
            ChangedByIdentity = await authorizer.GetSubjectAsync(),
            ChangedByName = null
        };
        await store.SetAsync(stored, cancellationToken);

        return await DescribeAsync(request.TeamKey, stored, cancellationToken);
    }

    public async Task<SupportAssistantLimit> ClearLimitAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        RequireTeam(teamKey);
        await store.DeleteForTeamAsync(teamKey, cancellationToken);
        return await DescribeAsync(teamKey, null, cancellationToken);
    }

    private async Task<SupportAssistantLimit> DescribeAsync(string teamKey, StoredSupportAssistantLimit stored, CancellationToken cancellationToken)
    {
        var kept = catalogue == null ? null : await catalogue.GetAsync(cancellationToken);
        var currency = kept?.Models.Select(x => x.Currency).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "USD";

        return new SupportAssistantLimit
        {
            TeamKey = teamKey,
            Limit = stored != null ? stored.Limit : options.DefaultLimit,
            IsOwn = stored != null,
            DefaultLimit = options.DefaultLimit,
            Currency = currency
        };
    }

    private static void RequireTeam(string teamKey)
    {
        if (string.IsNullOrWhiteSpace(teamKey)) throw new ArgumentException("A team is required.", nameof(teamKey));
    }
}

/// <summary>
/// The one place the limits are authorized: every operation requires <see cref="SystemSupportScopes.AllowanceManage"/>.
/// </summary>
internal sealed class AuthorizationSupportAssistantLimitServiceDecorator(
    ISupportAssistantLimitService inner,
    TeamAuthorizer authorizer) : ISupportAssistantLimitService
{
    public async Task<SupportAssistantLimit> GetLimitAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        await RequireAsync();
        return await inner.GetLimitAsync(teamKey, cancellationToken);
    }

    public async Task<SupportAssistantLimit> SetLimitAsync(SupportAssistantLimitRequest request, CancellationToken cancellationToken = default)
    {
        await RequireAsync();
        return await inner.SetLimitAsync(request, cancellationToken);
    }

    public async Task<SupportAssistantLimit> ClearLimitAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        await RequireAsync();
        return await inner.ClearLimitAsync(teamKey, cancellationToken);
    }

    private async Task RequireAsync()
    {
        if (!await authorizer.HasSystemScopeAsync(SystemSupportScopes.AllowanceManage))
            throw new UnauthorizedAccessException($"Changing the support assistant's limits requires the '{SystemSupportScopes.AllowanceManage}' system scope.");
    }
}

/// <summary>Audits every change to a team's limit under <c>assistant</c>, with the limit it was given. Wraps the authorizing decorator, so a refusal is recorded rather than lost.</summary>
internal sealed class AuditingSupportAssistantLimitServiceDecorator(
    ISupportAssistantLimitService inner,
    CompositeAuditLogger auditLogger,
    IAuditEntryFactory auditEntryFactory) : ISupportAssistantLimitService
{
    private const string Feature = "assistant";

    public Task<SupportAssistantLimit> GetLimitAsync(string teamKey, CancellationToken cancellationToken = default)
        => inner.GetLimitAsync(teamKey, cancellationToken);

    public Task<SupportAssistantLimit> SetLimitAsync(SupportAssistantLimitRequest request, CancellationToken cancellationToken = default)
        => AuditAsync("allowance-limit-set", nameof(SetLimitAsync), request?.TeamKey, request?.Limit?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none", () => inner.SetLimitAsync(request, cancellationToken));

    public Task<SupportAssistantLimit> ClearLimitAsync(string teamKey, CancellationToken cancellationToken = default)
        => AuditAsync("allowance-limit-clear", nameof(ClearLimitAsync), teamKey, "default", () => inner.ClearLimitAsync(teamKey, cancellationToken));

    private async Task<SupportAssistantLimit> AuditAsync(string action, string methodName, string teamKey, string limit, Func<Task<SupportAssistantLimit>> operation)
    {
        var sw = Stopwatch.StartNew();
        var metadata = new Dictionary<string, string> { [SupportAuditMetadataKeys.AssistantAllowanceLimit] = limit };
        try
        {
            var result = await operation();
            auditLogger.Log(auditEntryFactory.Create(Feature, action, methodName, sw.ElapsedMilliseconds, true, null, teamKey, metadata));
            return result;
        }
        catch (Exception ex)
        {
            auditLogger.Log(auditEntryFactory.Create(Feature, action, methodName, sw.ElapsedMilliseconds, false, ex.Message, teamKey, metadata));
            throw;
        }
    }
}

/// <summary>Removes a team's own limit when the team is purged.</summary>
internal sealed class SupportAssistantLimitPurgeParticipant(ISupportAssistantLimitStore store) : ITeamPurgeParticipant
{
    public string Name => "support assistant limit";

    public async Task<int> PurgeTeamDataAsync(string teamKey, CancellationToken cancellationToken = default)
        => await store.DeleteForTeamAsync(teamKey, cancellationToken) ? 1 : 0;
}

/// <summary>Stands in for the limit participant where no limit store is registered: there is nothing to purge.</summary>
internal sealed class NoLimitToPurge : ITeamPurgeParticipant
{
    public static readonly NoLimitToPurge Instance = new();

    public string Name => "support assistant limit";

    public Task<int> PurgeTeamDataAsync(string teamKey, CancellationToken cancellationToken = default) => Task.FromResult(0);
}
