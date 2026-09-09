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

    public RequireScopeAttribute(string scope)
    {
        Scope = scope;
    }
}
