using Tharga.Team;
using Tharga.Team.Blazor.Features.Authentication;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Top-level options for configuring the Tharga Team via a single AddThargaTeam call.
/// All sub-options have sensible defaults. Configure only what you need.
/// </summary>
public class ThargaTeamOptions
{
    internal Type _emailSenderType;
    internal Type _userDirectoryServiceType;
    internal Type _iconStoreType;
    internal readonly List<Type> _iconSourceTypes = [];
    internal readonly List<Type> _apiKeyLifecycleHandlers = [];

    /// <summary>
    /// Options for the Blazor UI layer (title, team service types, auth state decoration).
    /// </summary>
    public ThargaBlazorOptions Blazor { get; } = new();

    /// <summary>
    /// Options for Azure AD / OIDC authentication.
    /// </summary>
    public ThargaAuthOptions Auth { get; } = new();

    /// <summary>
    /// Options for API key authentication scheme.
    /// Set to null to skip API key authentication registration.
    /// </summary>
    public ApiKeyOptions ApiKey { get; set; } = new();

    /// <summary>
    /// Options for MVC controllers and Swagger. Set to null to skip.
    /// </summary>
    public ThargaControllerOptions Controllers { get; set; } = new();

    /// <summary>
    /// Configure scopes for access-level-based authorization.
    /// When null, scope registration is skipped and scope UI columns are hidden.
    /// </summary>
    public Action<ScopeRegistry> ConfigureScopes { get; set; }

    /// <summary>
    /// Configure tenant roles for role-based authorization within teams.
    /// When null, tenant role registration is skipped and role UI columns are hidden.
    /// </summary>
    public Action<TenantRoleRegistry> ConfigureTenantRoles { get; set; }

    /// <summary>
    /// Enable runtime-defined (dynamic) tenant roles: team administrators can create / update / delete their
    /// own custom roles per team (scopes constrained to app-registered scopes), managed via the
    /// <c>TenantRoleManager</c> component and surfaced alongside code-registered roles. When false (default),
    /// only code-registered roles apply and custom-role scopes are not resolved into claims.
    /// </summary>
    public bool EnableDynamicRoles { get; set; }

    /// <summary>
    /// The scope required to create / edit / delete a team's custom roles when <see cref="EnableDynamicRoles"/>
    /// is set (via the <c>TenantRoleManager</c> component and the service-layer authorization decorator).
    /// When null (default), <c>team:manage</c> is used. Set to a narrower scope (e.g. <c>access:manage</c>) to
    /// move custom-role administration onto a dedicated admin surface without granting the broad <c>team:manage</c>.
    /// </summary>
    public string DynamicRoleManageScope { get; set; }

    /// <summary>
    /// Configure system-level (global) scopes — the capabilities offered to system API keys (and, via role
    /// mapping, to privileged users). When null, system-scope registration is skipped.
    /// </summary>
    public Action<SystemScopeRegistry> ConfigureSystemScopes { get; set; }

    /// <summary>
    /// Map app/global roles (e.g. "Developer") to system scopes, so privileged users gain those scopes as
    /// claims (team-independent). When null, no role→system-scope mapping is applied.
    /// </summary>
    public Action<SystemRoleRegistry> ConfigureSystemRoles { get; set; }

    /// <summary>
    /// Options for audit logging. Set to null to skip audit registration.
    /// </summary>
    public AuditOptions Audit { get; set; }

    /// <summary>
    /// What deleting a team does. Defaults to <see cref="TeamDeleteMode.Soft"/> — the team is marked
    /// deleted and hidden from every read, and can be restored or purged afterwards.
    /// </summary>
    /// <remarks>
    /// <b>The default changed in 3.13.1.</b> Before it, <c>DeleteTeamAsync</c> removed the record and
    /// dropped the team's storage in one irreversible step. Nothing in the API changed and a deleted team
    /// still disappears from every read; what differs is that the document survives until purged, and that
    /// deleting no longer needs the elevated database privilege dropping storage requires
    /// (Tharga/Team#224).
    /// <para>
    /// Set it to <see cref="TeamDeleteMode.Hard"/> to keep the old behaviour — appropriate where a deletion
    /// must genuinely leave nothing behind, and where the deployment holds the privilege to drop a team's
    /// storage.
    /// </para>
    /// </remarks>
    public TeamDeleteMode TeamDeleteMode { get; set; } = TeamDeleteMode.Soft;

    /// <summary>
    /// Options for email sending (SMTP). When set and no custom email service is registered,
    /// the built-in SmtpTeamEmailSender is used. Set to null to disable email.
    /// </summary>
    public EmailOptions Email { get; set; }

    /// <summary>
    /// Register a custom email sender implementation. When set, this takes precedence
    /// over the built-in SMTP sender regardless of EmailOptions.
    /// </summary>
    public void AddEmailService<T>() where T : class, ITeamEmailSender
    {
        _emailSenderType = typeof(T);
    }

    /// <summary>
    /// Register a handler that receives an API key's private token on create and recycle/regenerate,
    /// plus a tokenless signal on delete (see <see cref="IApiKeyLifecycleHandler"/>). May be called
    /// multiple times to register several handlers. Requires <see cref="ApiKey"/> to be set.
    /// </summary>
    public void AddApiKeyLifecycleHandler<THandler>() where THandler : class, IApiKeyLifecycleHandler
    {
        _apiKeyLifecycleHandlers.Add(typeof(THandler));
    }

    /// <summary>
    /// Register a custom external user directory (<see cref="IUserDirectoryService"/>), enabling user
    /// verification, the directory-only user listing, and directory deletion in user administration.
    /// For Microsoft Entra ID, use <c>AddThargaEntraUserDirectory</c> from the Tharga.Team.Entra package
    /// instead. When no directory is registered, those features are unavailable and their UI is hidden.
    /// </summary>
    public void AddUserDirectoryService<T>() where T : class, IUserDirectoryService
    {
        _userDirectoryServiceType = typeof(T);
    }

    /// <summary>
    /// Limits applied when accepting an icon (max size, allowed content types, max dimension).
    /// </summary>
    public IconOptions Icon { get; set; } = new();

    /// <summary>
    /// Runtime-adjustable icon display/behavior settings (Gravatar on/off + style, a generic default
    /// image, whether user/admin uploads are allowed). This instance is registered as a singleton, so
    /// changes made to it at runtime take effect live.
    /// </summary>
    public IconSettings IconSettings { get; } = new();

    /// <summary>
    /// Replace the icon <b>storage</b> backend (<see cref="IIconStore"/> — where icon bytes live). When
    /// not set, the built-in <c>MongoIconStore</c> (from <c>AddThargaTeamRepository</c>) is used. Supply a
    /// custom store (Azure Blob, S3, an existing DMS, …) here.
    /// </summary>
    public void AddIconStore<T>() where T : class, IIconStore
    {
        _iconStoreType = typeof(T);
    }

    /// <summary>
    /// Add an icon <b>source</b> (<see cref="IIconSource"/> — where a displayed image comes from). May be
    /// called multiple times; sources run in registration order <i>after</i> the built-in
    /// <see cref="StoredIconSource"/>, so a platform-stored icon takes precedence and custom sources fill
    /// in when none is set.
    /// </summary>
    public void AddIconSource<T>() where T : class, IIconSource
    {
        _iconSourceTypes.Add(typeof(T));
    }
}
