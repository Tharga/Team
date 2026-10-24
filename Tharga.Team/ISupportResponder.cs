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

    /// <summary>
    /// Answers as <see cref="AnswerAsync(SupportCase, IReadOnlyList{SupportMessage}, CancellationToken)"/> does,
    /// reporting how far it has got while it works: the round, the tools running, and the answer as written so far.
    /// </summary>
    /// <remarks>
    /// <b>Each report is the whole state</b>, the text so far included, not what changed, so a reader that misses one
    /// still shows the answer as it stands. What is returned is what is kept: a report is a preview, and an answer
    /// that is then declined was never given.
    /// <para>
    /// A default member answering in one piece and reporting nothing, so a responder written before streaming keeps
    /// compiling and answering as it did.
    /// </para>
    /// </remarks>
    Task<SupportAnswer> AnswerAsync(SupportCase supportCase, IReadOnlyList<SupportMessage> transcript, IProgress<SupportAssistantProgress> progress, CancellationToken cancellationToken = default)
        => AnswerAsync(supportCase, transcript, cancellationToken);

    /// <summary>
    /// Whether this responder can answer for a team, and if not, why.
    /// </summary>
    /// <remarks>
    /// Asked before a case is handed to the assistant and before the assistant is offered, so a team the
    /// responder cannot answer for is never offered it. A default member answering
    /// <see cref="SupportAssistantReadiness.Ready"/>, so a responder written before readiness existed keeps
    /// compiling and keeps answering for every team, as it did.
    /// <para>
    /// Answer <see cref="SupportAssistantReadiness.Ready"/> or <see cref="SupportAssistantReadiness.NotConfigured"/>.
    /// <see cref="SupportAssistantReadiness.NotRegistered"/> means there is no responder, so from a responder it
    /// reads as ready: it is what a test double returns when it does not run this default.
    /// </para>
    /// <para>
    /// Throwing reads as <see cref="SupportAssistantReadiness.NotConfigured"/>, so a case is still raised, for a
    /// person, when the responder's settings cannot be read. Log the cause here; the caller cannot.
    /// </para>
    /// </remarks>
    Task<SupportAssistantReadiness> GetReadinessAsync(string teamKey, CancellationToken cancellationToken = default)
        => Task.FromResult(SupportAssistantReadiness.Ready);
}
