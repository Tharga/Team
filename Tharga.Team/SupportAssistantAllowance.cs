using System.ComponentModel;

namespace Tharga.Team;

/// <summary>How a team is shown what is left of its support assistant allowance.</summary>
/// <remarks>The limit is always kept as an amount of money; this only decides what a team reads.</remarks>
public enum SupportAllowanceDisplay
{
    /// <summary>Only the share used, as a bar and a percentage. No amount at all.</summary>
    Share,

    /// <summary>Units: the cost divided by the value of one unit, under a label the host chooses, such as "WU".</summary>
    Units,

    /// <summary>An estimate in tokens, at the rate the team's recent answers cost.</summary>
    Tokens,

    /// <summary>The amount of money, in the catalogue's currency.</summary>
    Money
}

/// <summary>What one platform model would cost the team, and how many questions that leaves it.</summary>
public sealed record SupportAssistantModelAllowance
{
    /// <summary>The <see cref="PlatformAiModel.Id"/>.</summary>
    public required string ModelId { get; init; }

    /// <summary>The name teams see.</summary>
    public string DisplayName { get; init; }

    /// <summary>Whether it is the model the team is answered by now.</summary>
    public bool Current { get; init; }

    /// <summary>What an answer costs on average, in <see cref="SupportAssistantAllowance.Unit"/>; null when nothing is known, and for a member who may not see amounts.</summary>
    public decimal? CostPerAnswer { get; init; }

    /// <summary>About how many more questions the allowance lasts on this model; null when it cannot be told.</summary>
    public long? QuestionsLeft { get; init; }
}

/// <summary>
/// What a team may spend on the support assistant in a period, and how much of it is spent.
/// </summary>
/// <remarks>
/// <b>Amounts are for the team's administrators.</b> A member is given <see cref="Share"/> and the state, and the amounts are null, because
/// what a team spends is its administrators' business and hiding a number in a component is not a permission.
/// </remarks>
public sealed record SupportAssistantAllowance
{
    /// <summary>The team.</summary>
    public required string TeamKey { get; init; }

    /// <summary>Whether the team is limited at all. False for a team on its own provider, which costs the platform nothing, and where no limit is set or the model has no price.</summary>
    public bool Applies { get; init; }

    /// <summary>What the amounts are shown as.</summary>
    public SupportAllowanceDisplay Display { get; init; }

    /// <summary>What the amounts are counted in: a unit label, "tokens" or a currency. Null with <see cref="SupportAllowanceDisplay.Share"/>.</summary>
    public string Unit { get; init; }

    /// <summary>The limit for the period, in <see cref="Unit"/>; null when it may not be shown.</summary>
    public decimal? Limit { get; init; }

    /// <summary>What is spent, in <see cref="Unit"/>; null when it may not be shown.</summary>
    public decimal? Used { get; init; }

    /// <summary>The share spent, 0 to 1, or above 1 when the allowance was overrun; null when the team is not limited.</summary>
    public decimal? Share { get; init; }

    /// <summary>The share at which a team is warned, such as 0.8.</summary>
    public decimal WarnAt { get; init; } = 0.8m;

    /// <summary>Whether the allowance is used up, so the assistant will not answer until the period ends or the limit is raised.</summary>
    public bool Exhausted { get; init; }

    /// <summary>When the period ends and the amount used starts again, in UTC.</summary>
    public DateTime PeriodEndsAt { get; init; }

    /// <summary>The platform models offered, with what each would cost the team.</summary>
    public SupportAssistantModelAllowance[] Models { get; init; } = [];

    /// <summary>The allowance as a member sees it: the share and the state, no amount.</summary>
    public SupportAssistantAllowance WithoutAmounts() => this with
    {
        Limit = null,
        Used = null,
        Models = [.. Models.Select(x => x with { CostPerAnswer = null })]
    };
}

/// <summary>
/// Where a team's allowance comes from. The contract a host implements to count what it counts, such as model use that does not go through
/// the support assistant. The default sums the assistant's own usage.
/// </summary>
/// <remarks>
/// <b>Internal, in the sense of the service layering:</b> deliberately unchecked, so the assistant can ask before it answers. Never inject it from a
/// component, a controller or a tool provider; <see cref="ISupportAssistantAllowanceService"/> is the checked way in. Return the full amounts; the
/// service takes them out for a member.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ISupportAssistantAllowanceSource
{
    /// <summary>The team's allowance; one where <see cref="SupportAssistantAllowance.Applies"/> is false when the team is not limited.</summary>
    Task<SupportAssistantAllowance> GetAsync(string teamKey, CancellationToken cancellationToken = default);
}

/// <summary>What a member and the team's administrators read of the assistant's allowance. Requires being a member of the team.</summary>
public interface ISupportAssistantAllowanceService
{
    /// <summary>The team's allowance. A member who does not hold <c>assistant:manage</c> on the team gets the share and the state without any amount.</summary>
    Task<SupportAssistantAllowance> GetAllowanceAsync(string teamKey, CancellationToken cancellationToken = default);
}

/// <summary>A team's own limit, set by whoever runs the product, as stored.</summary>
public sealed record StoredSupportAssistantLimit
{
    /// <summary>The team.</summary>
    public required string TeamKey { get; init; }

    /// <summary>The limit per period, in the catalogue's currency; null means the team has no limit, whatever the default is.</summary>
    public decimal? Limit { get; init; }

    /// <summary>When it was set.</summary>
    public DateTime ChangedAt { get; init; }

    /// <summary>Who set it, as the stable subject.</summary>
    public string ChangedByIdentity { get; init; }

    /// <summary>Who set it, by name when it was set.</summary>
    public string ChangedByName { get; init; }

    /// <summary>
    /// The key the default limit is kept under, beside the teams' own. No team has this key, so it can never be a team's own limit or be
    /// removed with one.
    /// </summary>
    public const string DefaultKey = "*";
}

/// <summary>Keeps each team's own limit. The persistence port behind <see cref="ISupportAssistantLimitService"/>.</summary>
public interface ISupportAssistantLimitStore
{
    /// <summary>The team's own limit, or null when it has none and is held to the default.</summary>
    Task<StoredSupportAssistantLimit> GetAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>Replaces the team's own limit.</summary>
    Task SetAsync(StoredSupportAssistantLimit limit, CancellationToken cancellationToken = default);

    /// <summary>Removes the team's own limit, so it is held to the default again; true when there was one.</summary>
    Task<bool> DeleteForTeamAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>Every limit kept: the teams' own and, under <see cref="StoredSupportAssistantLimit.DefaultKey"/>, the default when staff have set one.</summary>
    Task<StoredSupportAssistantLimit[]> GetAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>A team's limit as the platform's staff see it.</summary>
public sealed record SupportAssistantLimit
{
    /// <summary>The team.</summary>
    public required string TeamKey { get; init; }

    /// <summary>The limit that holds: the team's own when <see cref="IsOwn"/>, otherwise the default. Null means no limit.</summary>
    public decimal? Limit { get; init; }

    /// <summary>Whether the team has a limit of its own.</summary>
    public bool IsOwn { get; init; }

    /// <summary>The default every team without its own is held to: the one staff set, otherwise the host's option. Null means none.</summary>
    public decimal? DefaultLimit { get; init; }

    /// <summary>The currency of the amounts, from the catalogue; "USD" when there is none.</summary>
    public string Currency { get; init; }
}

/// <summary>Every limit staff manage: the default, and each team that has one of its own.</summary>
public sealed record SupportAssistantLimits
{
    /// <summary>The default that holds for a team with no limit of its own: <see cref="StoredDefault"/> when staff have set one, otherwise <see cref="OptionDefault"/>. Null means none.</summary>
    public decimal? DefaultLimit { get; init; }

    /// <summary>Whether staff have set the default, so it beats the host's option; false when it is the option that holds.</summary>
    public bool StoredDefault { get; init; }

    /// <summary>The default the host configured, which holds until staff set one. Null means none.</summary>
    public decimal? OptionDefault { get; init; }

    /// <summary>The currency of the amounts, from the catalogue; "USD" when there is none.</summary>
    public string Currency { get; init; }

    /// <summary>The teams that have a limit of their own, each with the amount (null for no limit at all).</summary>
    public SupportAssistantLimit[] Teams { get; init; } = [];
}

/// <summary>Setting the default limit.</summary>
public sealed record SupportAssistantDefaultLimitRequest
{
    /// <summary>The limit per period for every team without one of its own, in the catalogue's currency, from zero; null for no limit at all.</summary>
    public decimal? Limit { get; init; }
}

/// <summary>Setting a team's own limit.</summary>
public sealed record SupportAssistantLimitRequest
{
    /// <summary>The team.</summary>
    public required string TeamKey { get; init; }

    /// <summary>The limit per period, in the catalogue's currency, from zero; null for no limit at all.</summary>
    public decimal? Limit { get; init; }
}

/// <summary>
/// What the platform's staff do with the limits. Every operation requires <see cref="SystemSupportScopes.AllowanceManage"/>.
/// </summary>
/// <remarks>
/// A system scope, because a team's administrators raising their own limit is what the limit is there to prevent.
/// Operations rather than CRUD, with serializable inputs and outputs.
/// </remarks>
public interface ISupportAssistantLimitService
{
    /// <summary>The limit that holds for a team, and whether it is the team's own.</summary>
    Task<SupportAssistantLimit> GetLimitAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>Gives a team a limit of its own, or none. Refused for a negative limit.</summary>
    Task<SupportAssistantLimit> SetLimitAsync(SupportAssistantLimitRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes a team's own limit, so it is held to the default.</summary>
    Task<SupportAssistantLimit> ClearLimitAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>The default, and every team that has a limit of its own.</summary>
    Task<SupportAssistantLimits> GetLimitsAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets the default every team without a limit of its own is held to, or no limit at all. Beats the host's option until cleared. Refused for a negative limit.</summary>
    Task<SupportAssistantLimits> SetDefaultLimitAsync(SupportAssistantDefaultLimitRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes the default staff set, so the host's option holds again.</summary>
    Task<SupportAssistantLimits> ClearDefaultLimitAsync(CancellationToken cancellationToken = default);
}
