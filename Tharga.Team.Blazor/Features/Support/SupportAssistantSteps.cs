using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Features.Support;

/// <summary>What the assistant is doing, beside its dots, and the service calls it made, under its answer.</summary>
internal static class SupportAssistantSteps
{
    /// <summary>"Second lookup…" while its second service call runs, or null while none runs. <paramref name="text"/> holds <see cref="SupportTranscriptText"/>'s lookup texts.</summary>
    public static string Describe(TextSet text, SupportAssistantProgress progress)
    {
        if (progress?.Tools is not { Length: > 0 } || progress.Calls is not { Length: > 0 }) return null;

        var number = progress.Calls.Length;
        var ordinals = text[SupportTranscriptText.LookupOrdinals].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return number <= ordinals.Length
            ? text.Format(SupportTranscriptText.Lookup, ordinals[number - 1])
            : text.Format(SupportTranscriptText.LookupNumbered, number);
    }

    /// <summary>"2 service calls", for the list under an answer.</summary>
    public static string Counted(TextSet text, SupportAssistantCall[] calls)
        => calls.Length == 1 ? text[SupportTranscriptText.ServiceCall] : text.Format(SupportTranscriptText.ServiceCalls, calls.Length);

    /// <summary>One call as a row: <c>search_my_cases(text: "export")</c>.</summary>
    public static string Row(SupportAssistantCall call) => $"{call.Name}({call.Arguments})";
}
