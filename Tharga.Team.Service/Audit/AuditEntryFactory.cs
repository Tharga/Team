using Microsoft.AspNetCore.Http;

namespace Tharga.Team.Service.Audit;

/// <summary>
/// Builds an audit entry with the caller already resolved — an HTTP principal when there is one, the
/// declared <see cref="AuditActor"/> when there is not.
/// </summary>
/// <remarks>
/// Without this, a consumer writing its own entry constructs <see cref="AuditEntry"/> by hand and passes
/// it to <see cref="IAuditLogger.Log"/>, which never consults the ambient actor — so background work
/// could declare an actor and still write entries attributed to nobody. Build entries here and hand the
/// result to the logger.
/// </remarks>
public interface IAuditEntryFactory
{
    /// <summary>
    /// Builds an entry for a consumer-defined operation, with the caller filled in. The entry is recorded
    /// as <see cref="AuditEventType.ServiceCall"/>.
    /// </summary>
    /// <remarks>
    /// <b>Prefer the overload taking an <see cref="AuditEventType"/> for anything that changed something.</b>
    /// The enforcement proxies record their per-call access traces as <see cref="AuditEventType.ServiceCall"/>
    /// too, so an entry written through this overload cannot be told apart from them by the Event filter —
    /// and on a busy tenant the traces outnumber the domain entries several times over.
    /// </remarks>
    /// <param name="feature">The area acted on — the left half of a scope, e.g. <c>"job"</c>.</param>
    /// <param name="action">What was done — the right half, e.g. <c>"claim"</c>.</param>
    /// <param name="methodName">Optional method or step name, for the log's Method column.</param>
    /// <param name="durationMs">How long the operation took, if measured.</param>
    /// <param name="success">Whether it succeeded. False routes it to the failure styling in the log view.</param>
    /// <param name="errorMessage">Why it failed, shown in the failure tooltip and the exports.</param>
    /// <param name="teamKey">The team acted on. Supply it for background work — there is no selected team to infer.</param>
    /// <param name="metadata">What changed, surfaced in the log's detail row.</param>
    AuditEntry Create(
        string feature,
        string action,
        string methodName = null,
        long durationMs = 0,
        bool success = true,
        string errorMessage = null,
        string teamKey = null,
        IReadOnlyDictionary<string, string> metadata = null);

    /// <summary>
    /// Builds an entry classified as <paramref name="eventType"/>, so a reader can tell what someone did
    /// from what someone was permitted to call.
    /// </summary>
    /// <remarks>
    /// <b>The event type is what makes the log's Event filter useful to a consumer.</b> Without it every
    /// consumer-written entry lands as <see cref="AuditEventType.ServiceCall"/> — the same value the scope
    /// proxy writes for each authorized call — and the one distinction a reader cares about is the one the
    /// filter cannot express. Use <see cref="AuditEventType.DataChange"/> for an operation that altered
    /// something.
    /// <para>
    /// The event type does not change how the entry is stored, retained or authorized. It is a
    /// classification for reading, and it is free-standing: the enforcement proxies keep writing their own
    /// entries for the same call regardless.
    /// </para>
    /// </remarks>
    /// <param name="eventType">How to classify the entry, e.g. <see cref="AuditEventType.DataChange"/>.</param>
    /// <param name="feature">The area acted on — the left half of a scope, e.g. <c>"job"</c>.</param>
    /// <param name="action">What was done — the right half, e.g. <c>"claim"</c>.</param>
    /// <param name="methodName">Optional method or step name, for the log's Method column.</param>
    /// <param name="durationMs">How long the operation took, if measured.</param>
    /// <param name="success">Whether it succeeded. False routes it to the failure styling in the log view.</param>
    /// <param name="errorMessage">Why it failed, shown in the failure tooltip and the exports.</param>
    /// <param name="teamKey">The team acted on. Supply it for background work — there is no selected team to infer.</param>
    /// <param name="metadata">What changed, surfaced in the log's detail row.</param>
    AuditEntry Create(
        AuditEventType eventType,
        string feature,
        string action,
        string methodName = null,
        long durationMs = 0,
        bool success = true,
        string errorMessage = null,
        string teamKey = null,
        IReadOnlyDictionary<string, string> metadata = null);
}

/// <inheritdoc />
public sealed class AuditEntryFactory(IHttpContextAccessor httpContextAccessor) : IAuditEntryFactory
{
    public AuditEntry Create(
        string feature,
        string action,
        string methodName = null,
        long durationMs = 0,
        bool success = true,
        string errorMessage = null,
        string teamKey = null,
        IReadOnlyDictionary<string, string> metadata = null)
        => AuditHelper.BuildEntry(httpContextAccessor, feature, action, methodName, durationMs, success, errorMessage, teamKey, metadata);

    public AuditEntry Create(
        AuditEventType eventType,
        string feature,
        string action,
        string methodName = null,
        long durationMs = 0,
        bool success = true,
        string errorMessage = null,
        string teamKey = null,
        IReadOnlyDictionary<string, string> metadata = null)
        => AuditHelper.BuildEntry(httpContextAccessor, feature, action, methodName, durationMs, success, errorMessage, teamKey, metadata, eventType);
}
