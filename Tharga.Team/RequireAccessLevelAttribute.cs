namespace Tharga.Team;

/// <summary>
/// Declares the minimum access level required to call this method.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class RequireAccessLevelAttribute : Attribute
{
    public AccessLevel MinimumLevel { get; }

    /// <summary>
    /// Whether calls to this method are recorded in the audit log. Defaults to
    /// <see cref="AuditMode.Default"/>, which defers to the host's <c>AuditOptions.DefaultAuditMode</c>.
    /// </summary>
    /// <remarks>
    /// Behaves identically to <see cref="RequireScopeAttribute.Audit"/>. The two enforcement points are
    /// deliberately not allowed to differ on this: a method's audit rule should not depend on which kind
    /// of check happens to guard it.
    /// </remarks>
    public AuditMode Audit { get; set; }

    public RequireAccessLevelAttribute(AccessLevel minimumLevel)
    {
        MinimumLevel = minimumLevel;
    }
}
