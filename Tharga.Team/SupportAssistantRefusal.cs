namespace Tharga.Team;

/// <summary>
/// A team that was refused an answer from the assistant, handed to every <see cref="ISupportAssistantRefusalSink"/>.
/// </summary>
public sealed record SupportAssistantRefusal
{
    public string TeamKey { get; init; }

    /// <summary>The conversation whose question was refused.</summary>
    public string CaseId { get; init; }

    /// <summary>Why: <see cref="SupportAssistantReadiness.AllowanceUsed"/>, <see cref="SupportAssistantReadiness.ProviderRefused"/> or <see cref="SupportAssistantReadiness.Held"/>.</summary>
    public SupportAssistantReadiness Reason { get; init; }

    public DateTime RefusedAt { get; init; }

    /// <summary>The team's allowance, for <see cref="SupportAssistantReadiness.AllowanceUsed"/>, shown as the host chose; null otherwise.</summary>
    public SupportAssistantAllowance Allowance { get; init; }

    /// <summary>The host's own message, for <see cref="SupportAssistantReadiness.Held"/>; null otherwise.</summary>
    public string Message { get; init; }
}

/// <summary>
/// Told when the assistant refuses a team's question because the team has used its allowance, the provider has refused the account, or the host's
/// <see cref="ISupportAssistantGate"/> holds it back. Where a host sends itself a message, such as to its own people, to act on it.
/// </summary>
/// <remarks>
/// <b>Once per spell, not once per question.</b> A team that keeps asking while it is refused is told about once; the next time is after the assistant has
/// answered it again (or, for the allowance, in a new period). The memory is the process's, so a restart may tell once more.
/// <para>
/// <b>A sink that throws never changes the refusal.</b> The failure is logged and the other sinks are still told. Every registered sink is told; registering
/// none is the default, and does nothing.
/// </para>
/// </remarks>
public interface ISupportAssistantRefusalSink
{
    Task HandleAsync(SupportAssistantRefusal refusal, CancellationToken cancellationToken = default);
}

/// <summary>Why the host holds the assistant back for a team.</summary>
public sealed record SupportAssistantHold
{
    /// <summary>What the team is told in place of an answer, in the language the host writes for it.</summary>
    public string Message { get; init; }
}

/// <summary>
/// Lets the host refuse the assistant's answers for a team for reasons of its own, such as a plan that does not include it, before the model is asked.
/// </summary>
/// <remarks>
/// <b>Asked before every question and whenever readiness is read</b>, so keep it quick. Return null to let the assistant answer. A gate that throws lets the
/// assistant answer and is logged: the host's rule is not worth taking everybody's support away when it breaks. The message is shown by
/// <c>SupportPanel</c> in place of the conversation; its actions are the host's to place with the panel's <c>AssistantTop</c> content.
/// <para>Registering none is the default, and nothing is held.</para>
/// </remarks>
public interface ISupportAssistantGate
{
    Task<SupportAssistantHold> CheckAsync(string teamKey, CancellationToken cancellationToken = default);
}
