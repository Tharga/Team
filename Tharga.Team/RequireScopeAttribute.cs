namespace Tharga.Team;

/// <summary>
/// Declares the scope required to call this method.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class RequireScopeAttribute : Attribute
{
    public string Scope { get; }

    /// <summary>
    /// Whether calls to this method are recorded in the audit log. Defaults to
    /// <see cref="AuditMode.Default"/>, which defers to the host's <c>AuditOptions.DefaultAuditMode</c>.
    /// </summary>
    /// <remarks>
    /// The audit decision sits beside the access decision because that is where a reviewer looks, and
    /// because one attribute can only give one answer — two attributes on a method could disagree about
    /// the same call with no way to tell which wins.
    /// </remarks>
    public AuditMode Audit { get; set; }

    /// <summary>
    /// On a team service, also admits a caller holding this scope as a system grant, acting on the named team
    /// without being a member of it; defaults to <c>false</c>, and a system service rejects it at registration.
    /// </summary>
    public bool AllowSystemGrant { get; init; }

    public RequireScopeAttribute(string scope)
    {
        Scope = scope;
    }
}
