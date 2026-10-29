namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>SupportAssistantUsageView</c>: how much the support assistant was used, per team,
/// provider and model, for the people who run or bill the product.
/// </summary>
public static class SupportAssistantUsageViewText
{
    /// <summary>The start of the period, inclusive.</summary>
    public static readonly TextKey From = new("team.support.usage.from", "From");

    /// <summary>The end of the period, inclusive.</summary>
    public static readonly TextKey To = new("team.support.usage.to", "To");

    /// <summary>The team column.</summary>
    public static readonly TextKey Team = new("team.support.usage.team", "Team");

    /// <summary>The provider column.</summary>
    public static readonly TextKey Provider = new("team.support.usage.provider", "Provider");

    /// <summary>The model column.</summary>
    public static readonly TextKey Model = new("team.support.usage.model", "Model");

    /// <summary>The column counting answers.</summary>
    public static readonly TextKey Answers = new("team.support.usage.answers", "Answers");

    /// <summary>The column counting declines, which spent tokens too.</summary>
    public static readonly TextKey Declines = new("team.support.usage.declines", "Declined");

    /// <summary>The column counting the assistant's service calls.</summary>
    public static readonly TextKey ToolCalls = new("team.support.usage.toolCalls", "Service calls");

    /// <summary>The column of tokens the model read.</summary>
    public static readonly TextKey InputTokens = new("team.support.usage.inputTokens", "Input tokens");

    /// <summary>The column of input tokens read from the provider's cache.</summary>
    public static readonly TextKey CachedTokens = new("team.support.usage.cachedTokens", "Of which cached");

    /// <summary>The column of tokens the model wrote.</summary>
    public static readonly TextKey OutputTokens = new("team.support.usage.outputTokens", "Output tokens");

    /// <summary>The column of all tokens.</summary>
    public static readonly TextKey TotalTokens = new("team.support.usage.totalTokens", "Total tokens");

    /// <summary>The row adding up every row.</summary>
    public static readonly TextKey Total = new("team.support.usage.total", "Total");

    /// <summary>A provider or model that was not named.</summary>
    public static readonly TextKey Unknown = new("team.support.usage.unknown", "Unknown");

    /// <summary>When the assistant was not used in the period.</summary>
    public static readonly TextKey NoUsage = new("team.support.usage.noUsage", "The assistant was not used in this period.");

    /// <summary>When the reader may not read the usage.</summary>
    public static readonly TextKey NotPermitted = new("team.support.usage.notPermitted", "You cannot see the assistant's usage.");

    /// <summary>Every key the component renders, for building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        From, To, Team, Provider, Model, Answers, Declines, ToolCalls, InputTokens, CachedTokens, OutputTokens, TotalTokens, Total, Unknown,
        NoUsage, NotPermitted
    ];
}
