using System.Text.RegularExpressions;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Counts the user-facing strings a razor component still renders literally.
/// </summary>
/// <remarks>
/// <b>This replaces an attribute-only scan that understated the work by roughly three times.</b> The first
/// version matched `Text="…"` and friends, which made `TeamComponent` look like 24 strings. It has 24
/// attributes, 2 inline, and <b>64 built in the C# block</b> — the dialog titles, notifications and
/// confirmation prompts, which are most of what a user actually reads. A number that measures one category
/// while reading as "the remaining work" is worse than no number.
/// <para>
/// <b>Heuristic, and deliberately so.</b> Separating display text from identifiers, CSS classes and
/// component-library enum names cannot be done exactly without parsing. It does not need to be exact — it
/// needs to be <i>stable</i> and <i>directional</i>, so a ratchet built on it only ever moves the right way.
/// Excluded patterns are listed rather than tuned away, so a false exclusion is visible in review.
/// </para>
/// </remarks>
internal static class TextScan
{
    // Attribute text: Text="Save", Title="Members", title="…"
    private static readonly Regex Attribute =
        new(@"(?:Text|Title|Placeholder|title)=""([A-Z][^""{@]{2,})""", RegexOptions.Compiled);

    // Prose between tags, e.g. <RadzenText>You are not member of a team.</RadzenText>
    private static readonly Regex Inline =
        new(@">([A-Z][^<>@{}]{7,})<", RegexOptions.Compiled);

    // A string literal in the C# block that reads like a sentence or label.
    private static readonly Regex CodeString =
        new(@"\$?""([A-Z][a-z][^""]{6,})""", RegexOptions.Compiled);

    /// <summary>
    /// A razor comment. Not rendered, and prose by nature — the same case as an XML doc comment, and the
    /// largest remaining source of false positives once components keep their C# in the markup file: these
    /// blocks routinely quote the very strings the component renders when explaining a past defect.
    /// </summary>
    private static readonly Regex RazorComment = new(@"@\*.*?\*@", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// A <c>…Property="Member"</c> binding — <c>TextProperty</c>, <c>ValueProperty</c>,
    /// <c>DisabledProperty</c>. The value names a member of the bound item, never text shown to anyone.
    /// </summary>
    /// <remarks>
    /// Excluded at extraction rather than by value, because the value is indistinguishable from display
    /// text once separated from its attribute: <c>DisabledProperty="Inherited"</c> yields the perfectly
    /// plausible label "Inherited". Short member names slipped through only because the length floor hid
    /// them.
    /// </remarks>
    private static readonly Regex PropertyBinding = new(@"\w*Property=""[^""]*""", RegexOptions.Compiled);

    /// <summary>
    /// A Blazor generic type argument — <c>TValue="AccessLevel?"</c>, <c>TItem="TeamViewModel"</c>. A type
    /// name, never text shown to anyone.
    /// </summary>
    /// <remarks>
    /// Excluded at extraction for the same reason as <see cref="PropertyBinding"/>: once separated from its
    /// attribute the value is indistinguishable from a label. <c>AccessLevel?</c> reads exactly like a
    /// perfectly plausible column heading, and it was the last "literal" standing in <c>TeamComponent</c>
    /// after #204 — untranslatable by nature, so the scan was wrong rather than the component.
    /// </remarks>
    private static readonly Regex TypeArgument = new(@"T(?:Value|Item)=""[^""]*""", RegexOptions.Compiled);

    /// <summary>
    /// Not display text. Validated by running the scan over the already-migrated components: whatever it
    /// still reported there was, with one real exception, an identifier or a component-library enum name.
    /// <see cref="Identifier"/> also allows a trailing <c>()</c>, so a method name passed as data —
    /// <c>AddThargaAuditLogging()</c> — is recognised as the identifier it is.
    /// </summary>
    private static readonly Regex Identifier = new(@"^[A-Z][a-z]+([A-Z][a-z]*)+(\(\))?$", RegexOptions.Compiled);
    private static readonly Regex EnumQualified = new(@"^[A-Za-z]+\.[A-Za-z]", RegexOptions.Compiled);
    private static readonly Regex OtherNonDisplay = new(@"^[a-z]+:[a-z]|^[A-Za-z]+<|\.razor$|^rz-", RegexOptions.Compiled);

    /// <summary>
    /// A comma-separated field list — a CSV/JSON export header, which is an interchange format rather than
    /// display text.
    /// </summary>
    /// <remarks>
    /// <b>Excluded because translating it would be a defect, not a feature.</b> A downstream import matches
    /// these column names; a header that changed with the viewer's language would break every consumer's
    /// import the first time someone switched. So this is not the count being tuned down — it is a string
    /// that must stay literal, and the scan should stop asking for it to be keyed.
    /// </remarks>
    private static readonly Regex FieldList = new(@"^[A-Za-z]+(,[A-Za-z]+){3,}$", RegexOptions.Compiled);

    private static bool IsDisplayText(string value)
        => !Identifier.IsMatch(value) && !EnumQualified.IsMatch(value) && !OtherNonDisplay.IsMatch(value)
           && !FieldList.IsMatch(value);

    /// <summary>Every candidate display string in <paramref name="source"/>, deduplicated.</summary>
    public static IReadOnlyList<string> Candidates(string source)
    {
        var found = new List<string>();

        foreach (var line in RazorComment.Replace(source, string.Empty).Split('\n'))
        {
            // A comment of any kind is not rendered, and is prose by nature — the largest source of false
            // positives. XML docs were skipped from the start; a plain // line is the same case, missed
            // only because the first components scanned kept their C# in the .razor. Exception messages
            // are developer-facing, so they are not display text either.
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
            if (line.Contains("throw new", StringComparison.Ordinal)) continue;
            if (line.Contains("Exception(", StringComparison.Ordinal)) continue;

            var scannable = TypeArgument.Replace(PropertyBinding.Replace(line, string.Empty), string.Empty);

            foreach (Match m in Attribute.Matches(scannable)) found.Add(m.Groups[1].Value);
            foreach (Match m in Inline.Matches(scannable)) found.Add(m.Groups[1].Value);
            foreach (Match m in CodeString.Matches(scannable)) found.Add(m.Groups[1].Value);
        }

        return [.. found
            .Select(x => x.Trim())
            .Where(IsDisplayText)
            .Where(x => x.Length > 2)
            .Distinct(StringComparer.Ordinal)];
    }

    public static int Count(string source) => Candidates(source).Count;
}
