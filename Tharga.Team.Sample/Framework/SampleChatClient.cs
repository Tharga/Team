using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tharga.Team.Sample.Framework;

/// <summary>
/// A chat client that answers from a script, so the sample demonstrates the support assistant with no API
/// key, no network call and no vendor package.
/// </summary>
/// <remarks>
/// <b>This is the sample's stand-in, not something the toolkit ships.</b> What the toolkit consumes is
/// <see cref="IChatClient"/>; what produces one is the host's business. Swapping this for a real model is one
/// registration in <c>Program.cs</c> and touches nothing else — that is the whole point of the design, and
/// having the sample run on a fake is how it stays demonstrable on a machine with no model at all.
/// <para>
/// It answers deterministically and never claims to have fixed anything, which is also what the default
/// instructions tell a real model to do.
/// </para>
/// </remarks>
internal sealed class SampleChatClient : IChatClient
{
    // Says what it is, so nobody trying the sample mistakes a script for a model that could not help them.
    private const string Fallback =
        "I am the sample's built-in responder: a short script, not an AI model, so I only answer questions about " +
        "your cases, invitations, API keys, teams and exports. To try a real model, choose one for your team under Support, " +
        "AI assistant; or ask for a person.";

    private const string Signature = "(Answered by the sample's built-in scripted responder, not an AI model.)";

    private const string CasesTool = "get_my_cases";

    private const string SearchTool = "search_my_cases";

    private const string MissingSkillTool = "report_missing_skill";

    private const int MaxReportedLength = 80;

    private static readonly TimeSpan LookupDelay = TimeSpan.FromMilliseconds(800);

    private static readonly (string Term, string Answer)[] Script =
    [
        ("invit", "Invitation links expire after the lifetime the host configures, and an expired one can be extended without changing the code — so a link already sent keeps working. Ask whoever invited you to extend it rather than sending a new one."),
        ("api key", "An API key is shown once, when it is created. If it has been lost, create a new one and disable the old one rather than trying to recover it."),
        ("team", "You can see the teams you belong to from the team selector. If one is missing, you are probably not a member yet — an administrator of that team can invite you."),
        ("export", "An empty export is usually a filter that matches nothing rather than a failure. Check the date range first, and tell me what it is set to if that is not it.")
    ];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options = null,
        CancellationToken cancellationToken = default)
    {
        var history = messages?.ToList() ?? [];
        var question = history.LastOrDefault(x => x.Role == ChatRole.User)?.Text ?? string.Empty;

        // A question about cases looks things up as a model would: the member's cases, then, when the question names
        // something, the cases that mention it; then it answers from what it found.
        if (question.Contains("case", StringComparison.OrdinalIgnoreCase) && options?.Tools?.Any(x => x.Name == CasesTool) == true)
        {
            // Tool calls are not kept in the case, so any result here is from this answer's own lookups.
            var found = history.SelectMany(x => x.Contents).OfType<FunctionResultContent>().ToArray();
            if (found.Length == 0) return Call(CasesTool);

            var term = SearchTerm(question);
            var searches = term != null && options.Tools.Any(x => x.Name == SearchTool);
            if (searches && found.Length == 1) return Call(SearchTool, new Dictionary<string, object> { ["text"] = term });

            var answer = $"I looked up your support cases in this team: you have {Count(found[0])}.";
            if (searches) answer += $" {Count(found[1])} of them mention \"{term}\".";

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"{answer} Ask about one of them, or ask for a person.\n\n{Signature}")));
        }

        var scripted = Array.Find(Script, x => question.Contains(x.Term, StringComparison.OrdinalIgnoreCase)).Answer;

        // Off the script, it says so as a model should: report what was asked, then tell the member. The desk lists it.
        if (scripted == null && options?.Tools?.Any(x => x.Name == MissingSkillTool) == true
            && !history.SelectMany(x => x.Contents).OfType<FunctionResultContent>().Any())
        {
            var what = question.Length > MaxReportedLength ? question[..MaxReportedLength] : question;
            return Call(MissingSkillTool, new Dictionary<string, object> { ["what"] = what });
        }

        var reply = scripted == null ? Fallback : $"{scripted}\n\n{Signature}";

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
    }

    private static Task<ChatResponse> Call(string tool, IDictionary<string, object> arguments = null)
        => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(Guid.NewGuid().ToString("N"), tool, arguments)])));

    private static int Count(FunctionResultContent result)
        => result.Result is JsonElement { ValueKind: JsonValueKind.Array } list ? list.GetArrayLength() : 0;

    /// <summary>The last word worth searching for, such as "export" in "show me my case about the export"; null when there is none.</summary>
    private static string SearchTerm(string question)
        => System.Text.RegularExpressions.Regex.Matches(question, @"\p{L}{4,}")
            .Select(x => x.Value)
            .LastOrDefault(x => !NotSearchedFor.Contains(x));

    private static readonly HashSet<string> NotSearchedFor = new(StringComparer.OrdinalIgnoreCase)
    {
        "case", "cases", "about", "have", "many", "show", "find", "what", "which", "where", "with", "from", "that", "this",
        "there", "their", "your", "mine", "open", "does", "need", "help", "please", "could", "would", "list", "tell"
    };

    // A word at a time, at about the pace of a small local model, so the panel shows an answer being written.
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);

        foreach (var message in response.Messages)
        {
            var calls = message.Contents.OfType<FunctionCallContent>().ToArray();
            if (calls.Length > 0)
            {
                await Task.Delay(LookupDelay, cancellationToken);
                yield return new ChatResponseUpdate(message.Role, [.. calls]);
                await Task.Delay(LookupDelay, cancellationToken);
                continue;
            }

            foreach (var word in System.Text.RegularExpressions.Regex.Split(message.Text, @"(?<=\s)"))
            {
                await Task.Delay(WordDelay, cancellationToken);
                yield return new ChatResponseUpdate(message.Role, word);
            }
        }
    }

    private static readonly TimeSpan WordDelay = TimeSpan.FromMilliseconds(60);

    public object GetService(Type serviceType, object serviceKey = null) => null;

    public void Dispose()
    {
    }
}
