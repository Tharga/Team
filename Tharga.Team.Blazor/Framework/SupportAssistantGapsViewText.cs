namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>SupportAssistantGapsView</c>: what the assistant was asked and had no way to do,
/// for the people who build it.
/// </summary>
public static class SupportAssistantGapsViewText
{
    /// <summary>When the assistant has not yet been asked for anything it could not do.</summary>
    public static readonly TextKey NoGaps = new("team.support.gaps.none",
        "Nothing yet: the assistant has not been asked for anything it could not do.");

    /// <summary>How many cases one thing was asked in; <c>{0}</c> is the count.</summary>
    public static readonly TextKey Cases = new("team.support.gaps.cases", "Cases: {0}");

    /// <summary>When the reader may not read cases across every team, which is what the list is made of.</summary>
    public static readonly TextKey NotPermitted = new("team.support.gaps.notPermitted",
        "Seeing what the assistant could not do needs access to support cases across every team.");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [NoGaps, Cases, NotPermitted];
}
