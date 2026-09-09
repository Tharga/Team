using Tharga.Team;

namespace Tharga.Team.Service.Audit;

/// <summary>
/// Turns a method's declared <see cref="AuditMode"/> and the host's default into the two answers an
/// enforcement proxy needs: whether to write an entry, and what kind of event it is.
/// </summary>
/// <remarks>
/// <b>Shared by both proxies on purpose.</b> <c>ScopeProxy</c> and <c>AccessLevelProxy</c> intercept
/// different checks but must not answer this differently — a method's audit rule should not depend on which
/// kind of guard happens to be on it. Two copies of three lines is exactly how that drifts.
/// </remarks>
internal static class AuditModeResolver
{
    /// <summary>The method's own mode where it declared one, otherwise the host's.</summary>
    /// <remarks>
    /// A host default of <see cref="AuditMode.Default"/> cannot defer to itself, so it resolves to
    /// <see cref="AuditMode.Access"/> — the behaviour every consumer has today, which is the safe answer to
    /// a question nobody answered.
    /// </remarks>
    public static AuditMode Resolve(AuditMode declared, AuditMode hostDefault)
    {
        if (declared != AuditMode.Default) return declared;

        return hostDefault == AuditMode.Default ? AuditMode.Access : hostDefault;
    }

    /// <summary>
    /// Whether an entry is written. <see cref="AuditMode.None"/> suppresses the call but never the refusal.
    /// </summary>
    /// <remarks>
    /// <b>Suppressing an access trace says "do not record who read this". It does not say "do not record who
    /// was refused".</b> Those are different records with different purposes, and the privacy rules that
    /// motivate silence are about the first. Refusals are also low-volume by nature, so keeping them costs
    /// nothing a host would notice — while dropping them would quietly delete the evidence an audit log
    /// exists to hold.
    /// </remarks>
    public static bool ShouldWrite(AuditMode mode, bool denied)
        => denied || mode is AuditMode.Access or AuditMode.Change;

    /// <summary>
    /// The event type to record. A denial is always its own event, whatever the mode asked for — the mode
    /// says how to classify a call that happened, and a refused call did not happen.
    /// </summary>
    public static AuditEventType EventTypeFor(AuditMode mode, bool denied, AuditEventType denialType)
    {
        if (denied) return denialType;

        return mode == AuditMode.Change ? AuditEventType.DataChange : AuditEventType.ServiceCall;
    }
}
