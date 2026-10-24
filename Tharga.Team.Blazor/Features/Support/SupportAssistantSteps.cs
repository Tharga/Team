using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Features.Support;

/// <summary>What the assistant is doing, beside its dots, and the service calls it made, under its answer.</summary>
internal static class SupportAssistantSteps
{
    /// <summary>
    /// <paramref name="working"/> until the model has gone round the tool loop, then "Working 2…" with how many times it
    /// has. <paramref name="text"/> holds <see cref="SupportTranscriptText"/>.
    /// </summary>
    public static string Describe(TextSet text, SupportAssistantProgress progress, string working)
    {
        var loops = LoopsOf(progress);

        return loops > 0 ? text.Format(SupportTranscriptText.WorkingLoop, loops) : working;
    }

    /// <summary>Under text already written: "Working 2…" while a service call runs, or null while none does.</summary>
    public static string Running(TextSet text, SupportAssistantProgress progress)
        => progress?.Tools is { Length: > 0 } ? text.Format(SupportTranscriptText.WorkingLoop, LoopsOf(progress)) : null;

    /// <summary>How many times the model has asked for service calls: the round it is on while they run, the one before once they are back.</summary>
    public static int LoopsOf(SupportAssistantProgress progress)
    {
        if (progress == null) return 0;

        return progress.Tools is { Length: > 0 } ? progress.Round : progress.Round - 1;
    }

    /// <summary>"2 service calls", for the list under an answer.</summary>
    public static string Counted(TextSet text, SupportAssistantCall[] calls)
        => calls.Length == 1 ? text[SupportTranscriptText.ServiceCall] : text.Format(SupportTranscriptText.ServiceCalls, calls.Length);

    /// <summary>"2 service calls · 830 tokens", what the folded details under an answer say; each part only when there is one.</summary>
    public static string Summary(TextSet text, SupportAssistantCall[] calls, SupportAssistantUsage usage)
    {
        List<string> parts = [];
        if (calls.Length > 0) parts.Add(Counted(text, calls));
        if (usage != null) parts.Add(text.Format(SupportTranscriptText.Tokens, usage.TotalTokens.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)));

        return string.Join(" · ", parts);
    }

    /// <summary>One call as a row: <c>search_my_cases(text: "export")</c>.</summary>
    public static string Row(SupportAssistantCall call) => $"{call.Name}({call.Arguments})";
}
