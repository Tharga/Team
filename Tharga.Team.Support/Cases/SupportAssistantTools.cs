using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// The built-in read-only tools: the caller's own cases, a search through them, and one case's transcript.
/// </summary>
/// <remarks>
/// <b>Reads only, and deliberately few.</b> Closing, assigning and reopening stay with people — an assistant
/// that can close its own cases can close the ones it answered badly. The set is small because every tool is
/// surface the model can misuse, and because the tools worth having beyond this are the host's own.
/// </remarks>
/// <remarks>
/// <b>The case service is resolved when a tool runs, not injected.</b> It is what constructs the responder
/// that holds these tools, so taking it on the constructor would be a cycle in the container graph. By the
/// time a tool is called the scope is built and this returns the same instance the caller is using.
/// </remarks>
internal sealed class SupportAssistantTools(IServiceProvider services) : ISupportAssistantTools
{
    /// <summary>How many of the caller's newest cases a search reads; an assistant answers about recent work.</summary>
    internal const int MaxCasesSearched = 100;

    /// <summary>How many matches a search returns, so a broad word cannot fill the model's context.</summary>
    internal const int MaxMatches = 10;

    private const int SnippetLength = 160;
    private const int MinWordLength = 3;
    private const int PageSize = 50;

    public Task<IReadOnlyList<AITool>> ForCurrentCallerAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var cases = services.GetRequiredService<ISupportCaseService>();

        IReadOnlyList<AITool> tools =
        [
            AIFunctionFactory.Create(
                async (CancellationToken ct) =>
                {
                    var page = await cases.GetMyCasesAsync(teamKey, cancellationToken: ct);
                    return page.Items.Select(x => new { x.Id, x.Subject, Status = x.Status.ToString(), x.CreatedAt }).ToArray();
                },
                "get_my_cases",
                "The support cases raised by the person you are answering, newest first."),

            AIFunctionFactory.Create(
                (string text, CancellationToken ct) => SearchAsync(cases, teamKey, text, ct),
                "search_my_cases",
                "Finds the person's own support cases about something, by their subject and by everything written in them. " +
                "Pass a word or a few, such as \"joke\" or \"empty export\". Returns the best matches with the words that matched " +
                "and where; read one in full with get_case_messages."),

            AIFunctionFactory.Create(
                async (string caseId, CancellationToken ct) =>
                {
                    var page = await cases.GetMessagesAsync(teamKey, caseId, cancellationToken: ct);
                    return page.Items.Select(x => new { Author = x.AuthorName, x.Body, x.SentAt }).ToArray();
                },
                "get_case_messages",
                "The transcript of one of their cases, given its id from get_my_cases or search_my_cases.")
        ];

        return Task.FromResult(tools);
    }

    /// <remarks>
    /// <b>Through the same service as every other read</b>, so a search finds only cases the caller may read: their own.
    /// A case is read in full only when its subject does not already match every word.
    /// </remarks>
    internal static async Task<object> SearchAsync(ISupportCaseService cases, string teamKey, string text, CancellationToken cancellationToken)
    {
        var words = WordsOf(text);
        if (words.Length == 0) return "Pass a word to look for, of three letters or more.";

        List<SupportCase> mine = [];
        string cursor = null;
        do
        {
            var page = await cases.GetMyCasesAsync(teamKey, cursor, PageSize, cancellationToken);
            mine.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor != null && mine.Count < MaxCasesSearched);

        List<(SupportCase Case, string[] Matched, string Where)> found = [];
        foreach (var supportCase in mine.Take(MaxCasesSearched))
        {
            var inSubject = words.Where(w => Contains(supportCase.Subject, w)).ToArray();
            string where = inSubject.Length > 0 ? $"subject: {supportCase.Subject}" : null;
            var matched = inSubject;

            if (inSubject.Length < words.Length)
            {
                var messages = (await cases.GetMessagesAsync(teamKey, supportCase.Id, null, SupportCaseLimits.MaxMessagesPerCase, cancellationToken)).Items;
                var inBody = words.Except(inSubject).Where(w => messages.Any(m => Contains(m.Body, w))).ToArray();
                if (inBody.Length > 0)
                {
                    matched = [.. inSubject, .. inBody];
                    where ??= Snippet(messages.First(m => Contains(m.Body, inBody[0])).Body, inBody[0]);
                }
            }

            if (matched.Length > 0) found.Add((supportCase, matched, where));
        }

        if (found.Count == 0) return $"None of their cases mention {string.Join(" or ", words)}.";

        return found
            .OrderByDescending(x => x.Matched.Length)
            .ThenByDescending(x => x.Case.LastMessageAt ?? x.Case.CreatedAt)
            .Take(MaxMatches)
            .Select(x => new { x.Case.Id, x.Case.Subject, Status = x.Case.Status.ToString(), x.Case.CreatedAt, Matched = x.Matched, Where = x.Where })
            .ToArray();
    }

    /// <remarks>Words of three letters or more, each cut to a rough stem, so "telling" finds "tell" and "jokes" finds "joke".</remarks>
    internal static string[] WordsOf(string text)
        => [.. (text ?? string.Empty)
            .Split((char[])[' ', ',', '.', '?', '!', ':', ';', '"', '\'', '(', ')', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Stem(x.ToLowerInvariant()))
            .Where(x => x.Length >= MinWordLength)
            .Distinct()];

    private static string Stem(string word)
    {
        foreach (var ending in (string[])["ing", "ed", "es", "s"])
        {
            if (word.Length - ending.Length >= MinWordLength + 1 && word.EndsWith(ending, StringComparison.Ordinal))
                return word[..^ending.Length];
        }

        return word;
    }

    private static bool Contains(string value, string word) => value?.Contains(word, StringComparison.OrdinalIgnoreCase) == true;

    private static string Snippet(string body, string word)
    {
        var at = body.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        var start = Math.Max(0, at - (SnippetLength / 2));
        var length = Math.Min(SnippetLength, body.Length - start);

        return (start > 0 ? "…" : string.Empty) + body.Substring(start, length).Trim() + (start + length < body.Length ? "…" : string.Empty);
    }
}
