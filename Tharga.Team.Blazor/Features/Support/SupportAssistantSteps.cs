using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Features.Support;

/// <summary>Which round the assistant is on, and the tools it is running, as one line.</summary>
internal static class SupportAssistantSteps
{
    /// <summary>"Round 2: get_my_cases", or null when nothing has been reported. <paramref name="text"/> holds <see cref="SupportTranscriptText"/>'s round texts.</summary>
    public static string Describe(TextSet text, SupportAssistantProgress progress)
    {
        if (progress == null) return null;

        return progress.Tools is { Length: > 0 }
            ? text.Format(SupportTranscriptText.RoundTools, progress.Round, string.Join(", ", progress.Tools))
            : text.Format(SupportTranscriptText.Round, progress.Round);
    }
}
