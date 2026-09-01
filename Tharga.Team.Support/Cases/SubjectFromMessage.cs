namespace Tharga.Team.Support.Cases;

/// <summary>
/// Derives a case subject from the first words of its message.
/// </summary>
/// <remarks>
/// <b>A subject is a small tax on somebody who has already typed the problem into the box below it</b>, and
/// what it usually collects is a worse version of the first sentence. So it is optional, and when it is
/// absent the message supplies one.
/// <para>
/// <b>Derived here rather than in a component.</b> A host writing its own UI gets the same subject without
/// having to know it needed one — and <see cref="SupportCase.Subject"/> stays non-null, so nothing
/// downstream (a list, a mail subject line, a Slack heading) has to cope with a case that has none.
/// </para>
/// </remarks>
internal static class SubjectFromMessage
{
    /// <summary>Ellipsis marking a subject that is a summary rather than the whole message.</summary>
    private const string Ellipsis = "…";

    /// <summary>
    /// The first <see cref="SupportCaseLimits.DerivedSubjectLength"/> characters of
    /// <paramref name="body"/>, cut at a word boundary.
    /// </summary>
    /// <remarks>
    /// <b>Whitespace is collapsed before anything is measured</b>, and that ordering is the whole of it: a
    /// message opening with a blank line, or one pasted with hard-wrapped newlines, would otherwise produce a
    /// subject that is empty or full of line breaks. Both look like a bug in the list that renders them.
    /// </remarks>
    public static string Derive(string body)
    {
        var text = Collapse(body);

        if (text.Length == 0) return string.Empty;
        if (text.Length <= SupportCaseLimits.DerivedSubjectLength) return text;

        var cut = text[..SupportCaseLimits.DerivedSubjectLength];
        var lastSpace = cut.LastIndexOf(' ');

        // A single word longer than the limit has no boundary to cut at, so it is cut mid-word rather than
        // thrown away — a subject of nothing is worse than a subject of half a word.
        if (lastSpace > 0) cut = cut[..lastSpace];

        return cut.TrimEnd() + Ellipsis;
    }

    private static string Collapse(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;

        return string.Join(' ', body.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
    }
}
