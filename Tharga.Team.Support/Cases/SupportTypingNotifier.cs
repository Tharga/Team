using Microsoft.Extensions.Logging;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// In-process implementation of <see cref="ISupportTypingNotifier"/>.
/// </summary>
/// <remarks>
/// In-process for the same reason as <see cref="SupportCaseNotifier"/>, and with the same consequence: on several
/// instances, only circuits on the instance the writer is connected to see the signal.
/// </remarks>
internal sealed class SupportTypingNotifier(ILogger<SupportTypingNotifier> logger = null) : ISupportTypingNotifier
{
    public event EventHandler<SupportCaseTypingEventArgs> Typing;

    public void Notify(SupportCaseTypingEventArgs args)
    {
        var handlers = Typing;
        if (handlers == null) return;

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<SupportCaseTypingEventArgs>>())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "A support typing handler threw. Only that listener missed the signal.");
            }
        }
    }
}
