using System.Security.Claims;
using Tharga.Team;

namespace Tharga.Team.Service.Audit;

/// <summary>
/// Audit entries for the two events that happen outside any service call: an interactive sign-in, and the
/// user record created as a side effect of a first one.
/// </summary>
/// <remarks>
/// <b>Why these were missing.</b> Every other audited action passes through a service the auditing decorators
/// wrap. These two do not: a sign-in completes inside the authentication handshake, and a first-sign-in user
/// record is created while resolving the caller. So the audit log could say what someone did but never that
/// they arrived — and <c>Tharga.Team.Support</c> could not route the two events Tharga/Team#142 names first,
/// "user logs on" and "user created", because neither existed to route.
/// <para>
/// Built here rather than at the call sites so the shape is defined once and both are testable without an
/// authentication pipeline.
/// </para>
/// </remarks>
public static class AuthAuditEntries
{
    /// <summary>Feature name both entries carry, matching the API-key handler's <c>auth</c>.</summary>
    public const string Feature = "auth";

    public const string SignInAction = "signin";
    public const string UserCreatedAction = "user-created";

    /// <summary>An interactive sign-in completing.</summary>
    /// <remarks>
    /// <see cref="AuditCallerType.User"/> and <see cref="AuditCallerSource.Web"/> — the counterpart to the
    /// API-key handler's <c>auth/apikey</c> entry, which is <see cref="AuditCallerType.ApiKey"/>.
    /// No team: sign-in precedes team selection, so naming one here would be an invention.
    /// </remarks>
    public static AuditEntry SignIn(ClaimsPrincipal principal)
        => new()
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.AuthSuccess,
            Feature = Feature,
            Action = SignInAction,
            MethodName = nameof(SignIn),
            Success = true,
            CallerType = AuditCallerType.User,
            CallerSource = AuditCallerSource.Web,
            CallerIdentity = Identity(principal),
            CallerUserIdentity = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
        };

    /// <summary>
    /// A user record created because someone signed in for the first time.
    /// </summary>
    /// <remarks>
    /// <see cref="AuditEventType.DataChange"/> rather than an auth event: the sign-in is reported separately,
    /// and this is a write. The actor is the new user themselves — nobody else asked for it, which is what
    /// distinguishes it from an administrator creating a user through <c>IUserManagementService</c>.
    /// </remarks>
    public static AuditEntry UserCreated(IUser user, ClaimsPrincipal principal)
        => new()
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.DataChange,
            Feature = Feature,
            Action = UserCreatedAction,
            MethodName = nameof(UserCreated),
            Success = true,
            CallerType = AuditCallerType.User,
            CallerSource = AuditCallerSource.Web,
            CallerIdentity = user?.Identity ?? Identity(principal),
            CallerUserIdentity = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            Metadata = Describe(user)
        };

    private static string Identity(ClaimsPrincipal principal)
        => principal?.FindFirst(ClaimTypes.Email)?.Value
           ?? principal?.FindFirst(ClaimTypes.Upn)?.Value
           ?? principal?.Identity?.Name;

    private static Dictionary<string, string> Describe(IUser user)
    {
        if (user == null) return null;

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(user.Key)) metadata["user.key"] = user.Key;
        if (!string.IsNullOrEmpty(user.EMail)) metadata["user.email"] = user.EMail;

        return metadata.Count > 0 ? metadata : null;
    }
}
