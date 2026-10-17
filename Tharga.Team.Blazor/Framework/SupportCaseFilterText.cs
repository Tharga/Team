namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>SupportCaseFilter</c> — the filter every case list shares.
/// </summary>
public static class SupportCaseFilterText
{
    public static readonly TextKey Open = new("team.support.filter.open", "Open");

    public static readonly TextKey Closed = new("team.support.filter.closed", "Closed");

    public static readonly TextKey Support = new("team.support.filter.support", "Support");

    public static readonly TextKey Assistant = new("team.support.filter.assistant", "Assistant");

    /// <summary>Names the status choice for a screen reader.</summary>
    public static readonly TextKey StatusLabel = new("team.support.filter.status", "Status");

    /// <summary>Names the conversation-kind choice for a screen reader.</summary>
    public static readonly TextKey KindLabel = new("team.support.filter.kind", "Conversation");

    /// <summary>The search box's placeholder, and its name for a screen reader.</summary>
    public static readonly TextKey Search = new("team.support.filter.search", "Search");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Open, Closed, Support, Assistant, StatusLabel, KindLabel, Search];
}
