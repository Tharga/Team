namespace Tharga.Team;

/// <summary>
/// Answers a support case on behalf of an assistant.
/// </summary>
/// <remarks>
/// <b>A port, in the domain's language.</b> It names cases and answers; it knows nothing of models, prompts,
/// tokens or chat completions. That is what keeps the choice of provider — a local model, a self-hosted one,
/// or a hosted API — entirely the host's, and keeps every one of those words out of this package.
/// <para>
/// <b>It answers; it does not act.</b> An implementation reads to answer well, but closing, assigning and
/// reopening a case stay with people. An assistant that can close its own cases can close the ones it
/// answered badly.
/// </para>
/// <para>
/// <b>Declining is an answer.</b> A responder that cannot help says so and the case goes to a person, which
/// is an ordinary outcome rather than a failure — so it is a returned value and not an exception.
/// </para>
/// </remarks>
public interface ISupportResponder
{
    /// <summary>
    /// Answers the newest message on a case, or declines to.
    /// </summary>
    /// <remarks>
    /// <b>The case and its transcript are handed in, not fetched.</b> The responder is given exactly what the
    /// authorized caller had already read, so it cannot reach further even if it tried — an assistant with a
    /// wider view than its user would be a data leak wearing a helpful face. It also keeps the port free of
    /// the service that calls it, which would otherwise be a dependency cycle.
    /// </remarks>
    Task<SupportAnswer> AnswerAsync(SupportCase supportCase, IReadOnlyList<SupportMessage> transcript, CancellationToken cancellationToken = default);
}
