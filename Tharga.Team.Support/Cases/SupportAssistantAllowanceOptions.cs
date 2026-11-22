namespace Tharga.Team.Support.Cases;

/// <summary>
/// What a team may spend on the support assistant's platform model, and how it is shown.
/// </summary>
/// <remarks>
/// <b>Nothing is limited until a limit is set.</b> A team on its own provider costs the platform nothing and is never limited. The period is a calendar
/// month in UTC. A single team can be given a limit of its own through <see cref="ISupportAssistantLimitService"/>, which beats <see cref="DefaultLimit"/>.
/// </remarks>
public class SupportAssistantAllowanceOptions
{
    /// <summary>
    /// What a team may spend per period unless it has a limit of its own, in the catalogue's currency. Null, the default, limits no team.
    /// </summary>
    public decimal? DefaultLimit { get; set; }

    /// <summary>What a team reads: the share only, units, an estimate in tokens, or money. Default <see cref="SupportAllowanceDisplay.Units"/>, so the amount of money is not what a team sees.</summary>
    public SupportAllowanceDisplay Display { get; set; } = SupportAllowanceDisplay.Units;

    /// <summary>What one unit is worth, in the catalogue's currency; with <see cref="SupportAllowanceDisplay.Units"/> a team reads cost divided by this. Default 0.0001, so a unit is a hundredth of a cent.</summary>
    public decimal UnitValue { get; set; } = 0.0001m;

    /// <summary>What a unit is called, such as "WU". Default "units".</summary>
    public string UnitLabel { get; set; } = "units";

    /// <summary>The share at which a team is warned. Default 0.8.</summary>
    public decimal WarnAt { get; set; } = 0.8m;

    /// <summary>How many days back the cost of an answer is averaged over, to tell how many questions are left. Default 30.</summary>
    public int AverageOverDays { get; set; } = 30;
}
