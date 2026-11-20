namespace Tharga.Team;

/// <summary>
/// Receives an assistant conversation its person rated, to deliver wherever the host analyses them, such as its support inbox.
/// </summary>
/// <remarks>
/// <b>Every registered sink is told</b>, once the rating is stored, for each rating the host's options let through:
/// thumbs-down only by default. Registering none is the default, and does nothing.
/// <para>
/// <b>A sink that throws never fails the rating.</b> The failure is logged and the other sinks are still told; the rating is
/// kept either way.
/// </para>
/// <para>Write it with <see cref="SupportConversationFeedbackJson"/>, so every host delivers the same shape.</para>
/// </remarks>
public interface ISupportConversationFeedbackSink
{
    /// <summary>Delivers one rated conversation.</summary>
    Task HandleAsync(SupportConversationFeedback feedback, CancellationToken cancellationToken = default);
}
