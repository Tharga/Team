namespace Tharga.Team.Support.Cases;

/// <summary>How an answer mail quotes the conversation so far.</summary>
public enum SupportQuoteStyle
{
    /// <summary>
    /// As Gmail does: one "On &lt;date&gt;, &lt;name&gt; wrote:" line and one quoted block with a bar at its left, holding the latest
    /// earlier entry, with the older ones nested inside it.
    /// </summary>
    Nested,

    /// <summary>One attribution line and one quoted block for each earlier entry, side by side, as earlier versions did.</summary>
    PerEntry
}
