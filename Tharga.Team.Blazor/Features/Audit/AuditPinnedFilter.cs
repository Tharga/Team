using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Features.Audit;

/// <summary>
/// Locks one or more <see cref="AuditLogView"/> filter dimensions to a fixed value.
/// Pinned dimensions are <b>hidden</b> from the filter bar — the caller cannot change them, so showing
/// a disabled control is only clutter — and are forced into the underlying <see cref="AuditQuery"/>
/// regardless of any in-component filter state.
/// Use to scope the audit log to a single API key, team, caller, etc.
/// </summary>
/// <remarks>
/// The component does not announce the scope it is showing. The host chose it and can describe it in its
/// own words, with names rather than the keys pinned here.
/// </remarks>
public sealed record AuditPinnedFilter
{
    /// <summary>Pin to a specific API key Guid string.</summary>
    public string CallerKeyId { get; init; }

    /// <summary>Pin to a specific caller type (e.g. <see cref="AuditCallerType.ApiKey"/>).</summary>
    public AuditCallerType? CallerType { get; init; }

    /// <summary>Pin to a specific team key.</summary>
    public string TeamKey { get; init; }

    /// <summary>Pin to a specific caller identity (substring match, case-insensitive).</summary>
    public string CallerIdentity { get; init; }

    /// <summary>Pin to the acting user's authentication subject. Exact match — prefer this over <see cref="CallerIdentity"/> where the subject is known.</summary>
    public string CallerUserIdentity { get; init; }

    /// <summary>
    /// Pin to what was done to something — one of <see cref="AuditTargetKinds"/> — rather than to who did it. Use with <see cref="TargetKey"/>
    /// for "what has been done to this user". Entries written before targets were recorded have none and are not shown.
    /// </summary>
    public string TargetKind { get; init; }

    /// <summary>Pin to the key of what was done to — the user's key, the team's key or the API key's id. Exact match.</summary>
    public string TargetKey { get; init; }

    /// <summary>Pin to a single feature.</summary>
    public string Feature { get; init; }

    /// <summary>Pin to a single action.</summary>
    public string Action { get; init; }
}
