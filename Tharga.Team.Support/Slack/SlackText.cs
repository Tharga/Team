namespace Tharga.Team.Support.Slack;

/// <summary>
/// Makes text safe to place inside a Slack message.
/// </summary>
/// <remarks>
/// Slack reads <c>&lt;</c>, <c>&gt;</c> and <c>&amp;</c> as markup — links, mentions, channel references — so a
/// value somebody typed, such as a team name or a case subject, could otherwise break the message or put a
/// link in it. Slack's own escaping rule covers exactly these three characters; formatting such as <c>*bold*</c>
/// is left alone.
/// </remarks>
public static class SlackText
{
    /// <summary>The value with Slack's three control characters escaped; <c>null</c> stays <c>null</c>.</summary>
    public static string Escape(string value)
        => value?.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
