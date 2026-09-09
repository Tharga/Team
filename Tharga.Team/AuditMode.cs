namespace Tharga.Team;

/// <summary>
/// Whether a call through an enforcement proxy is recorded in the audit log, declared on the method
/// rather than configured centrally.
/// </summary>
/// <remarks>
/// <b>What separates a read worth recording from a read that is noise is intent, and only the declaration
/// site knows it.</b> Opening a case is worth recording; fetching the case-type list to draw a dropdown is
/// not — and both go through the same kind of scope-checked method, so no global rule can tell them apart.
/// That is why this sits on the attribute that already carries the access decision, next to the method it
/// governs.
/// <para>
/// A method that says nothing gets <see cref="Default"/>, which defers to
/// <c>AuditOptions.DefaultAuditMode</c>. <b>Deferring is deliberately the zero value</b>: an unannotated
/// method must mean "the host has not been asked", never a decision taken on the host's behalf.
/// </para>
/// <para>
/// <b>A refusal is recorded whatever this says.</b> Suppressing an access trace is a statement about
/// recording who read something; it is not a statement about concealing who was refused. Denials are
/// written under every mode.
/// </para>
/// </remarks>
public enum AuditMode
{
    /// <summary>Use the host's <c>AuditOptions.DefaultAuditMode</c>. What an unannotated method gets.</summary>
    Default,

    /// <summary>Not recorded. Denials are still written.</summary>
    None,

    /// <summary>Recorded as an access trace — <c>AuditEventType.ServiceCall</c>.</summary>
    Access,

    /// <summary>
    /// Recorded as a change — <c>AuditEventType.DataChange</c>, so it survives a host filter that drops
    /// service calls, and the log view's Event filter can separate it from access traces.
    /// </summary>
    Change
}
