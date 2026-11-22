using System.Diagnostics;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Support.Cases;

/// <summary>Gives a team a spend limit of its own, sets the default every other team is held to, and says which limit holds.</summary>
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
        RequireNotNegative(request.Limit);

        var stored = await StoreAsync(request.TeamKey, request.Limit, cancellationToken);
        return await DescribeAsync(request.TeamKey, stored, cancellationToken);
    }

    public async Task<SupportAssistantLimit> ClearLimitAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        RequireTeam(teamKey);
        await store.DeleteForTeamAsync(teamKey, cancellationToken);
        return await DescribeAsync(teamKey, null, cancellationToken);
    }

    public async Task<SupportAssistantLimits> GetLimitsAsync(CancellationToken cancellationToken = default)
    {
        var all = await store.GetAllAsync(cancellationToken);
        var currency = await CurrencyAsync(cancellationToken);
        var stored = all.FirstOrDefault(x => x.TeamKey == StoredSupportAssistantLimit.DefaultKey);

        return new SupportAssistantLimits
        {
            DefaultLimit = stored != null ? stored.Limit : options.DefaultLimit,
            StoredDefault = stored != null,
            OptionDefault = options.DefaultLimit,
            Currency = currency,
            Teams =
            [
                .. all.Where(x => x.TeamKey != StoredSupportAssistantLimit.DefaultKey)
                    .OrderBy(x => x.TeamKey, StringComparer.Ordinal)
                    .Select(x => new SupportAssistantLimit
                    {
                        TeamKey = x.TeamKey,
                        Limit = x.Limit,
                        IsOwn = true,
                        DefaultLimit = stored != null ? stored.Limit : options.DefaultLimit,
                        Currency = currency
                    })
            ]
        };
    }

    public async Task<SupportAssistantLimits> SetDefaultLimitAsync(SupportAssistantDefaultLimitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireNotNegative(request.Limit);

        await StoreAsync(StoredSupportAssistantLimit.DefaultKey, request.Limit, cancellationToken);
        return await GetLimitsAsync(cancellationToken);
    }

    public async Task<SupportAssistantLimits> ClearDefaultLimitAsync(CancellationToken cancellationToken = default)
    {
        await store.DeleteForTeamAsync(StoredSupportAssistantLimit.DefaultKey, cancellationToken);
        return await GetLimitsAsync(cancellationToken);
    }

    private async Task<StoredSupportAssistantLimit> StoreAsync(string key, decimal? limit, CancellationToken cancellationToken)
    {
        var stored = new StoredSupportAssistantLimit
        {
            TeamKey = key,
            Limit = limit,
            ChangedAt = timeProvider.GetUtcNow().UtcDateTime,
            ChangedByIdentity = await authorizer.GetSubjectAsync(),
            ChangedByName = null
        };
        await store.SetAsync(stored, cancellationToken);
        return stored;
    }

    private async Task<SupportAssistantLimit> DescribeAsync(string teamKey, StoredSupportAssistantLimit stored, CancellationToken cancellationToken)
    {
        var defaultLimit = (await AllowanceLimits.DefaultAsync(store, options, cancellationToken)).Limit;

        return new SupportAssistantLimit
        {
            TeamKey = teamKey,
            Limit = stored != null ? stored.Limit : defaultLimit,
            IsOwn = stored != null,
            DefaultLimit = defaultLimit,
            Currency = await CurrencyAsync(cancellationToken)
        };
    }

    private async Task<string> CurrencyAsync(CancellationToken cancellationToken)
    {
        var kept = catalogue == null ? null : await catalogue.GetAsync(cancellationToken);
        return kept?.Models.Select(x => x.Currency).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "USD";
    }

    /// <remarks>The default's key is the store's, never a team's, so it cannot be set, read or cleared as if it were one.</remarks>
    private static void RequireTeam(string teamKey)
    {
        if (string.IsNullOrWhiteSpace(teamKey)) throw new ArgumentException("A team is required.", nameof(teamKey));
        if (teamKey == StoredSupportAssistantLimit.DefaultKey) throw new ArgumentException("That is not a team.", nameof(teamKey));
    }

    private static void RequireNotNegative(decimal? limit)
    {
        if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit), "A limit cannot be negative.");
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

    public async Task<SupportAssistantLimits> GetLimitsAsync(CancellationToken cancellationToken = default)
    {
        await RequireAsync();
        return await inner.GetLimitsAsync(cancellationToken);
    }

    public async Task<SupportAssistantLimits> SetDefaultLimitAsync(SupportAssistantDefaultLimitRequest request, CancellationToken cancellationToken = default)
    {
        await RequireAsync();
        return await inner.SetDefaultLimitAsync(request, cancellationToken);
    }

    public async Task<SupportAssistantLimits> ClearDefaultLimitAsync(CancellationToken cancellationToken = default)
    {
        await RequireAsync();
        return await inner.ClearDefaultLimitAsync(cancellationToken);
    }

    private async Task RequireAsync()
    {
        if (!await authorizer.HasSystemScopeAsync(SystemSupportScopes.AllowanceManage))
            throw new UnauthorizedAccessException($"Changing the support assistant's limits requires the '{SystemSupportScopes.AllowanceManage}' system scope.");
    }
}

/// <summary>Audits every change to a limit under <c>assistant</c>, with the limit it was given. Wraps the authorizing decorator, so a refusal is recorded rather than lost.</summary>
internal sealed class AuditingSupportAssistantLimitServiceDecorator(
    ISupportAssistantLimitService inner,
    CompositeAuditLogger auditLogger,
    IAuditEntryFactory auditEntryFactory) : ISupportAssistantLimitService
{
    private const string Feature = "assistant";

    public Task<SupportAssistantLimit> GetLimitAsync(string teamKey, CancellationToken cancellationToken = default)
        => inner.GetLimitAsync(teamKey, cancellationToken);

    public Task<SupportAssistantLimits> GetLimitsAsync(CancellationToken cancellationToken = default)
        => inner.GetLimitsAsync(cancellationToken);

    public Task<SupportAssistantLimit> SetLimitAsync(SupportAssistantLimitRequest request, CancellationToken cancellationToken = default)
        => AuditAsync("allowance-limit-set", nameof(SetLimitAsync), request?.TeamKey, Text(request?.Limit), () => inner.SetLimitAsync(request, cancellationToken));

    public Task<SupportAssistantLimit> ClearLimitAsync(string teamKey, CancellationToken cancellationToken = default)
        => AuditAsync("allowance-limit-clear", nameof(ClearLimitAsync), teamKey, "default", () => inner.ClearLimitAsync(teamKey, cancellationToken));

    public Task<SupportAssistantLimits> SetDefaultLimitAsync(SupportAssistantDefaultLimitRequest request, CancellationToken cancellationToken = default)
        => AuditAsync("allowance-default-set", nameof(SetDefaultLimitAsync), null, Text(request?.Limit), () => inner.SetDefaultLimitAsync(request, cancellationToken));

    public Task<SupportAssistantLimits> ClearDefaultLimitAsync(CancellationToken cancellationToken = default)
        => AuditAsync("allowance-default-clear", nameof(ClearDefaultLimitAsync), null, "option", () => inner.ClearDefaultLimitAsync(cancellationToken));

    private static string Text(decimal? limit) => limit?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";

    private async Task<T> AuditAsync<T>(string action, string methodName, string teamKey, string limit, Func<Task<T>> operation)
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
        => teamKey == StoredSupportAssistantLimit.DefaultKey ? 0 : await store.DeleteForTeamAsync(teamKey, cancellationToken) ? 1 : 0;
}

/// <summary>Stands in for the limit participant where no limit store is registered: there is nothing to purge.</summary>
internal sealed class NoLimitToPurge : ITeamPurgeParticipant
{
    public static readonly NoLimitToPurge Instance = new();

    public string Name => "support assistant limit";

    public Task<int> PurgeTeamDataAsync(string teamKey, CancellationToken cancellationToken = default) => Task.FromResult(0);
}
