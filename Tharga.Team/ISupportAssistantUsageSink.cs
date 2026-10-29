namespace Tharga.Team;

/// <summary>
/// Receives a <see cref="SupportAssistantUsageRecord"/> every time the support assistant is asked.
/// </summary>
/// <remarks>
/// <b>Every registered sink is told</b>, so a host can keep usage in the toolkit's store and also forward it to its own
/// billing. Records come from the support service, not the responder, so a host's own <c>ISupportResponder</c> is
/// recorded too, by returning <see cref="SupportAnswer.Usage"/>.
/// <para>
/// <b>A sink that throws never fails the answer.</b> The failure is logged and the other sinks are still told: the
/// customer's answer matters more than the record of it.
/// </para>
/// </remarks>
public interface ISupportAssistantUsageSink
{
    /// <summary>Keeps one record.</summary>
    Task RecordAsync(SupportAssistantUsageRecord record, CancellationToken cancellationToken = default);
}
