namespace Tharga.Team.Support.Cases;

/// <summary>
/// A team's allowance from what the assistant itself recorded: the cost of this calendar month's answers against the team's own limit, or the default.
/// </summary>
/// <remarks>
/// <b>Not limited</b> when the team is not answered by a priced platform model (its own provider, off, or no price), when no limit holds, or when no
/// usage store is registered. The cost is summed in whatever currency the records were priced in; a catalogue that changes currency mid-period is a
/// decision for the host to handle with its own source. A host that also spends on the model elsewhere replaces this with
/// <see cref="ISupportAssistantAllowanceSource"/>.
/// </remarks>
internal sealed class DefaultSupportAssistantAllowanceSource(
    SupportAssistantAllowanceOptions options,
    TimeProvider timeProvider,
    ISupportAssistantUsageStore usage = null,
    ISupportAssistantLimitStore limits = null,
    ISupportAssistantPricing pricing = null,
    IPlatformAiCatalogueStore catalogue = null) : ISupportAssistantAllowanceSource
{
    public async Task<SupportAssistantAllowance> GetAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);

        var notLimited = new SupportAssistantAllowance { TeamKey = teamKey, Display = options.Display, PeriodEndsAt = end, WarnAt = options.WarnAt };
        if (string.IsNullOrEmpty(teamKey) || usage == null || pricing == null) return notLimited;

        var current = await pricing.GetAsync(teamKey, cancellationToken);
        if (current == null) return notLimited;

        var own = limits == null ? null : await limits.GetAsync(teamKey, cancellationToken);
        var limit = own != null ? own.Limit : options.DefaultLimit;
        if (limit == null) return notLimited;

        var thisPeriod = await usage.GetTotalsAsync(start, end, teamKey, cancellationToken);
        var priced = thisPeriod.Where(x => x.Cost != null).ToArray();
        var used = priced.Sum(x => x.Cost!.Value);
        var tokens = priced.Sum(x => x.TotalTokens);

        var models = await ModelsAsync(teamKey, current, limit.Value - used, now, end, cancellationToken);

        var (factor, unit) = Scale(current.Currency, used, tokens);
        var exhausted = used >= limit.Value;

        return notLimited with
        {
            Applies = true,
            Unit = options.Display == SupportAllowanceDisplay.Share ? null : unit,
            Limit = factor == null ? null : limit.Value * factor,
            Used = factor == null ? null : used * factor,
            Share = limit.Value > 0 ? used / limit.Value : 1m,
            Exhausted = exhausted,
            Models = [.. models.Select(x => factor == null ? x with { CostPerAnswer = null } : x with { CostPerAnswer = x.CostPerAnswer * factor })]
        };
    }

    /// <summary>What the amounts are multiplied by to be shown, and what they are counted in; no factor when they are not shown.</summary>
    private (decimal? Factor, string Unit) Scale(string currency, decimal used, long tokens)
        => options.Display switch
        {
            SupportAllowanceDisplay.Money => (1m, currency ?? "USD"),
            SupportAllowanceDisplay.Units => (options.UnitValue > 0 ? 1m / options.UnitValue : null, options.UnitLabel),
            SupportAllowanceDisplay.Tokens => (used > 0 && tokens > 0 ? tokens / used : null, "tokens"),
            _ => (null, null)
        };

    /// <summary>The offered models with what an answer on each costs, in money, and how many questions the rest of the allowance buys.</summary>
    private async Task<SupportAssistantModelAllowance[]> ModelsAsync(string teamKey, PlatformAiModel current, decimal remaining, DateTime now, DateTime end, CancellationToken cancellationToken)
    {
        var kept = catalogue == null ? null : await catalogue.GetAsync(cancellationToken);
        var offered = kept?.Models.Where(x => x.Offered).ToArray() ?? [current];
        if (offered.All(x => x.Id != current.Id)) offered = [current, .. offered];

        var from = now.AddDays(-Math.Max(1, options.AverageOverDays));
        var team = await usage.GetTotalsAsync(from, end, teamKey, cancellationToken);
        var everyone = team.Length > 0 && offered.All(x => AveragePerAnswer(team, x) != null) ? team : await usage.GetTotalsAsync(from, end, null, cancellationToken);

        return [.. offered.Select(x =>
        {
            var cost = AveragePerAnswer(team, x) ?? AveragePerAnswer(everyone, x);
            return new SupportAssistantModelAllowance
            {
                ModelId = x.Id,
                DisplayName = x.DisplayName ?? x.ModelId,
                Current = x.Id == current.Id,
                CostPerAnswer = cost,
                QuestionsLeft = cost is > 0 ? (long)Math.Floor(Math.Max(0, remaining) / cost.Value) : null
            };
        })];
    }

    /// <summary>What an answer on the model cost on average in the totals; null when it was not used or never priced.</summary>
    private static decimal? AveragePerAnswer(SupportAssistantUsageTotal[] totals, PlatformAiModel model)
    {
        var rows = totals.Where(x => x.Cost != null && x.Model == model.ModelId).ToArray();
        var answers = rows.Sum(x => x.Answers + x.Declines);
        return answers == 0 ? null : rows.Sum(x => x.Cost!.Value) / answers;
    }
}
