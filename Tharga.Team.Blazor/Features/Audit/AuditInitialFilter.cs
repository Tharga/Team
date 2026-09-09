using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Features.Audit;

/// <summary>
/// Opening filter values for <see cref="AuditLogView"/> — what the reader sees before touching anything,
/// and may then change.
/// </summary>
/// <remarks>
/// <b>This is the opposite of <see cref="AuditPinnedFilter"/>, not a variant of it.</b> A pin is a scoping
/// statement: it hides its control and forces the query, so a dialog opened on one API key shows that key
/// and nothing else. This is a reading convenience: the control stays visible and the reader can widen it.
/// The two are separate types because they carry different dimensions for that reason — pinning
/// <c>CallerKeyId</c> is meaningful, defaulting it would offer a scope the reader could simply switch off.
/// <para>
/// <b>A pin wins wherever both name the same dimension.</b> Not a precedence preference but a property the
/// pin depends on: a default is by definition changeable, so a default that could override a pin would let
/// the reader edit their way out of the scope the pin exists to impose.
/// </para>
/// <para>
/// <b>Defaults never narrow the filter options.</b> The dropdowns are built from the log inside the pinned
/// scope only, so a category hidden by a default still appears in its control — otherwise the reader could
/// not discover what was hidden, let alone switch it back on, and the default would be a pin wearing a
/// different name.
/// </para>
/// </remarks>
public sealed record AuditInitialFilter
{
    /// <summary>Event types selected on open. Empty or null opens on all of them.</summary>
    public AuditEventType[] EventTypes { get; init; }

    /// <summary>Scope features selected on open. Ignored when <see cref="AuditPinnedFilter.Feature"/> is pinned.</summary>
    public string[] Features { get; init; }

    /// <summary>Scope actions selected on open. Ignored when <see cref="AuditPinnedFilter.Action"/> is pinned.</summary>
    public string[] Actions { get; init; }

    /// <summary>
    /// Scopes hidden on open — the per-call access traces a reader is not looking for, named directly.
    /// </summary>
    /// <remarks>
    /// <b>Entries that checked no scope are unaffected</b>, so hiding <c>audit:read</c> removes the audit
    /// log's own readers while leaving every entry the application wrote. Setting this reveals a toggle in
    /// the filter bar that shows the hidden entries again; without it the reader would have no way to see
    /// that anything was held back.
    /// </remarks>
    public string[] ExcludedScopes { get; init; }
}
