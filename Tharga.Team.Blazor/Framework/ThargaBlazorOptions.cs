using Tharga.Blazor.Framework;
using Tharga.Team;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Blazor.Framework;

public record ThargaBlazorOptions : BlazorOptions
{
    internal Type _teamService;
    internal Type _userService;
    internal Type _memberType;
    internal Type _apiKeyService;
    internal Type _claimsEnricher;
    internal Type _textProvider;
    internal Type _iconStoreType;
    internal Type _emailSenderType;
    internal readonly List<Type> _iconSourceTypes = [];
    internal readonly List<TeamMenuItem> _menuItems = [];

    /// <summary>
    /// Adds an entry to the profile menu, after the built-in ones and above Logout.
    /// </summary>
    /// <remarks>
    /// The label is a key plus an English default, so it resolves through <see cref="IThargaTextProvider"/>
    /// exactly like the built-in entries — a host that registered a text provider gets this translated with no
    /// further work.
    /// <para>
    /// <b><paramref name="requiredScope"/> and <paramref name="requiredRole"/> control rendering, not
    /// access.</b> They hide a link the caller cannot use; the page behind it must still gate itself.
    /// </para>
    /// </remarks>
    /// <param name="icon">Material icon name, e.g. <c>help</c>.</param>
    /// <param name="textKey">Stable lookup key, e.g. <c>myapp.menu.help</c>.</param>
    /// <param name="defaultText">English text used when no translation is registered for the key.</param>
    /// <param name="href">Where the item navigates.</param>
    /// <param name="requiredScope">Optional scope the caller must hold for the item to render.</param>
    /// <param name="requiredRole">Optional role the caller must be in for the item to render.</param>
    public void AddMenuItem(string icon, string textKey, string defaultText, string href, string requiredScope = null, string requiredRole = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(icon);
        ArgumentException.ThrowIfNullOrWhiteSpace(textKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(href);

        _menuItems.Add(new TeamMenuItem(icon, new TextKey(textKey, defaultText), href, requiredScope, requiredRole));
    }

    /// <summary>
    /// Icon upload limits — maximum bytes and permitted content types.
    /// </summary>
    public IconOptions Icon { get; set; } = new();

    /// <summary>
    /// SMTP settings for the built-in email sender. When set — and no custom sender is registered via
    /// <see cref="AddEmailService{T}"/> — <c>SmtpTeamEmailSender</c> is registered as
    /// <see cref="ITeamEmailSender"/>. Leave null to send no email.
    /// </summary>
    /// <remarks>
    /// <b>This is invitation delivery and nothing else</b>
    /// (<see cref="ITeamEmailSender.SendInviteAsync"/>) — not a mail pipeline the rest of the toolkit shares.
    /// Support cases send and read mail through their own configuration in <c>Tharga.Team.Support</c>;
    /// setting this does not give them a mailbox, and setting theirs does not send invitations.
    /// <para>
    /// <see cref="EmailOptions.FromName"/> falls back to the application <c>Title</c> when not set.
    /// </para>
    /// </remarks>
    public EmailOptions Email { get; set; }

    /// <summary>
    /// Send invitations through your own implementation instead of SMTP. Takes precedence over
    /// <see cref="Email"/>, which is then ignored.
    /// </summary>
    /// <remarks>
    /// Registered scoped. Prefer this over configuring SMTP when the host already has a mail pipeline —
    /// the toolkit only ever asks it to send an invitation.
    /// </remarks>
    public void AddEmailService<T>() where T : class, ITeamEmailSender
    {
        _emailSenderType = typeof(T);
    }

    /// <summary>
    /// Runtime-adjustable icon behaviour: Gravatar on/off and style, a default image, upload toggles.
    /// </summary>
    /// <remarks>
    /// Registered as a singleton and mutable at runtime, so the instance itself is the contract — the
    /// facade assigns its own object here rather than copying values, to keep a host's later changes
    /// visible to the resolver.
    /// </remarks>
    public IconSettings IconSettings { get; internal set; } = new();

    /// <summary>
    /// Replace the icon <b>storage</b> backend (<see cref="IIconStore"/> — where icon bytes live). When
    /// not set, the built-in <c>MongoIconStore</c> (from <c>AddThargaTeamRepository</c>) is used.
    /// </summary>
    public void AddIconStore<T>() where T : class, IIconStore
    {
        _iconStoreType = typeof(T);
    }

    /// <summary>
    /// Add an icon <b>source</b> (<see cref="IIconSource"/> — where a displayed image comes from). May be
    /// called more than once; sources are consulted in registration order after
    /// <see cref="StoredIconSource"/>, so a stored icon takes precedence and custom sources fill in.
    /// </summary>
    public void AddIconSource<T>() where T : class, IIconSource
    {
        _iconSourceTypes.Add(typeof(T));
    }

    /// <summary>
    /// Automatically create the first team for users.
    /// Default is false.
    /// </summary>
    public bool AutoCreateFirstTeam { get; set; } = false;

    /// <summary>
    /// Allow users to create and delete teams via the UI.
    /// When false, the "Create team" and "Delete team" buttons are hidden.
    /// Independent of AutoCreateFirstTeam (system behavior).
    /// Default is true.
    /// </summary>
    public bool AllowTeamCreation { get; set; } = true;

    /// <summary>
    /// Fail startup when the host's user service leaves a persistence extension point un-overridden,
    /// instead of logging an error. Default <c>false</c>.
    /// </summary>
    /// <remarks>
    /// The gap is real either way: each un-overridden member accepts a write, reports success and
    /// discards it. The default logs rather than throws because the condition is pre-existing wherever it
    /// occurs, so throwing would turn a routine upgrade into an outage over a feature the host may never
    /// use. Set this where a silently-discarded write is worse than a failed boot.
    /// </remarks>
    public bool ThrowOnIncompleteUserService { get; set; }

    /// <summary>
    /// Throw at startup when a team-service facet cannot be resolved, instead of logging an error.
    /// Default false.
    /// </summary>
    /// <remarks>
    /// Off by default for the same reason <see cref="ThrowOnIncompleteUserService"/> is: the gap is
    /// pre-existing in every case that matters, and turning a routine upgrade into a boot failure is a
    /// worse trade than making it unmissable in the log.
    /// </remarks>
    public bool ThrowOnIncompleteTeamService { get; set; }

    /// <summary>
    /// Write a display name back to the external directory when an administrator renames a user.
    /// Default <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Which side owns display names is a per-host decision. A host federating from a corporate directory
    /// wants the directory authoritative and would be alarmed to find the application overwriting it; an
    /// application that collects no attributes at sign-up is the opposite case — it holds the real name
    /// while the directory holds a placeholder, and the good name cannot reach anyone administering the
    /// tenant.
    /// <para>
    /// Applies to the <b>administrative</b> rename only. Self-service renaming stays local whatever this
    /// is set to: a user editing their own display name here should not silently rewrite the
    /// organization's directory.
    /// </para>
    /// <para>
    /// The local write always happens. A directory failure is reported on
    /// <see cref="UserNameChangeResult"/>, never rolled back — the two fail independently, and coupling
    /// them would let a directory outage block renaming a user in this application.
    /// </para>
    /// </remarks>
    public bool WriteNameToDirectory { get; set; }

    /// <summary>
    /// Optional route that the built-in "Create team" entry points navigate to instead of
    /// performing the bare create. When set, the teamless "Create team" link in
    /// <c>TeamSelector</c> and the "Create new Team" button in <c>TeamComponent</c> redirect
    /// here — letting a host route team creation into its own onboarding flow while keeping
    /// <see cref="AllowTeamCreation"/> <c>true</c> so the programmatic create API still works.
    /// A per-component <c>CreateTeamRequested</c> callback, when supplied, takes precedence
    /// over this path. When <c>null</c> (default), the built-in behavior is unchanged.
    /// </summary>
    public string CreateTeamPath { get; set; }

    /// <summary>
    /// Optional route the profile menu's built-in <b>User</b> item navigates to, instead of
    /// <c>profile</c>. Set it when the host mounts <c>UserProfileView</c> somewhere else.
    /// </summary>
    /// <remarks>
    /// The toolkit ships the profile <i>component</i> and the host chooses the route, so the two could
    /// disagree with no way to reconcile them: the menu item navigated to a literal, and a host mounting
    /// the page at, say, <c>/account</c> got a menu item leading nowhere. Host-supplied menu items were
    /// never affected — they carry their own <c>Href</c>.
    /// <para>
    /// <c>null</c> (the default) keeps the built-in route, so nothing changes for an existing host.
    /// </para>
    /// </remarks>
    public string ProfilePath { get; set; }

    /// <summary>
    /// Optional route the profile menu's built-in <b>Team</b> item navigates to, instead of <c>team</c>.
    /// Set it when the host mounts <c>TeamComponent</c> somewhere else.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="ProfilePath"/>, and separate from it on purpose: the two pages are
    /// mounted independently, so one being moved says nothing about the other.
    /// <para>
    /// Distinct from <see cref="InvitePath"/>, which points at a route carrying <c>TeamInviteView</c> for
    /// people redeeming an invitation — a different capability with a different audience, which is why
    /// they are not one setting.
    /// </para>
    /// </remarks>
    public string TeamPath { get; set; }

    /// <summary>
    /// Optional route that generated invitation links point at, instead of <c>/team</c>.
    /// </summary>
    /// <remarks>
    /// Redeeming an invitation and administering a team are different capabilities with different
    /// audiences, but the generated link sent them to the same URL — so a host that gated <c>/team</c>
    /// for its own staff closed the one page that redeems an invite to precisely the people who needed
    /// it (Tharga/Team#191). It failed silently from every angle: a normal-looking link, a
    /// "not found"-shaped refusal that reads as an expired invitation, and nothing server-side, because
    /// the request never reached the invite handling at all.
    /// <para>
    /// Point this at a route carrying <c>&lt;TeamInviteView&gt;</c> and nothing more than
    /// <c>[Authorize]</c>. <b>Gating it any further reproduces the same failure at a new URL.</b>
    /// </para>
    /// <para>
    /// Both link paths go through one builder, so this covers the invitation email <i>and</i> the
    /// "Copy invitation link" action. The clipboard is the half a host cannot reach on its own — an
    /// <c>ITeamEmailSender</c> can rewrite what it sends, but not what an administrator copies.
    /// </para>
    /// When <c>null</c> (default), links point at <c>/team</c> as before.
    /// </remarks>
    public string InvitePath { get; set; }

    /// <summary>
    /// Data-access consent options (cross-team access granted by a team to global roles).
    /// </summary>
    public ConsentOptions Consent { get; set; } = new();

    /// <summary>
    /// Access levels the built-in selectors will not offer. Empty by default, which is exactly today's
    /// behaviour.
    /// </summary>
    /// <remarks>
    /// <b>Hidden is not invalid.</b> This governs what a person can <i>choose</i> — the invite and member
    /// dialogs, the API-key level, and the consent picker. It does not change what the model accepts, what a
    /// claim resolves to, or what <c>AccessLevelBadge</c> renders. A host syncing members from another system
    /// can still receive a hidden level, and those members keep working and keep showing their level.
    /// <para>
    /// The case it was built for: a host whose scopes are all registered at <c>Administrator</c> finds
    /// <see cref="AccessLevel.Viewer"/> and <see cref="AccessLevel.User"/> resolve to exactly the same
    /// thing, so offering both is a choice with nothing behind it that every team administrator has to
    /// reason about (Tharga/Team#232).
    /// </para>
    /// <para>
    /// A collection rather than a flags enum on purpose: <see cref="AccessLevel"/> is not
    /// <c>[Flags]</c> and <see cref="AccessLevel.Owner"/> is <c>0</c>, which in flag arithmetic means "no
    /// bits" — it could never be expressed, and a parallel flags enum would be a second list of levels to
    /// keep in sync.
    /// </para>
    /// <para>
    /// Validated at registration. Hiding <see cref="AccessLevel.Owner"/> throws, because no selector offers
    /// it and a setting that silently does nothing is worse than an error; so does any configuration that
    /// leaves a selector with nothing to choose. Hiding <see cref="AccessLevel.Administrator"/> is allowed
    /// but means management can only be delegated by transferring ownership.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// o.Blazor.HiddenAccessLevels = [AccessLevel.Viewer];
    /// </code>
    /// </example>
    public IReadOnlyCollection<AccessLevel> HiddenAccessLevels { get; set; } = [];

    /// <summary>
    /// Periodic revalidation of team claims for live Blazor Server circuits. Team membership, access
    /// level, tenant-role scopes, and consent-derived access are otherwise computed once when the circuit
    /// is established and stay frozen for its life — so a removed member, a downgraded access level, or a
    /// revoked consent is not applied (to the UI or to service-layer authorization) until a full reload.
    /// When enabled, the claims are re-evaluated on the configured interval and refreshed in place if they
    /// changed. Server/SSR only (applies when <see cref="SkipAuthStateDecoration"/> is <c>true</c>).
    /// </summary>
    public ClaimRevalidationOptions ClaimRevalidation { get; set; } = new();

    /// <summary>
    /// Lets a team administrator view the application as a less privileged user. Off by default.
    /// </summary>
    public Features.Simulation.AccessSimulationOptions Simulation { get; set; } = new();

    /// <summary>
    /// Controls how team/scope claims are enriched on the principal.
    /// <para>
    /// <b>true (default)</b> — Claims are enriched server-side via <c>IClaimsTransformation</c>,
    /// which reads the <c>selected_team_id</c> cookie. Works for Blazor Server, SSR, and Hybrid apps.
    /// No JS interop is used. This is the recommended setting for most applications.
    /// </para>
    /// <para>
    /// <b>false</b> — Additionally registers a client-side <c>AuthenticationStateProvider</c> decorator
    /// that enriches claims via LocalStorage/JS interop. Only needed for standalone Blazor WebAssembly
    /// apps with no server-side HTTP pipeline. Setting this to false on a Server/SSR app will cause
    /// a blank page (silent deadlock from JS interop during prerendering).
    /// </para>
    /// <para>
    /// <b>Note:</b> the <c>false</c> path has never been verified against a real standalone WebAssembly
    /// app — no WASM sample exists in this repository. Treat it as unproven. Automatic hosting-model
    /// detection was investigated so this option could be removed entirely, but four approaches all
    /// produced the same silent SSR hang and the work was dropped; the evidence pointed at the
    /// <c>AuthenticationStateProvider</c> decoration pattern itself rather than at when it is applied.
    /// </para>
    /// </summary>
    public bool SkipAuthStateDecoration { get; set; } = true;

    /// <summary>
    /// Add types for team and user services.
    /// </summary>
    /// <typeparam name="TServiceBase"></typeparam>
    /// <typeparam name="TUserService"></typeparam>
    /// <summary>
    /// Registers the host's team and user services, using the <b>standard member type</b>.
    /// </summary>
    /// <remarks>
    /// Use this when a team member needs no properties of your own. The member type is taken from
    /// <typeparamref name="TServiceBase"/> when it declares one — a service deriving from
    /// <c>TeamServiceRepositoryBase&lt;TEntity, TMember&gt;</c> does — so a host with its own member type
    /// gets that type here without naming it twice.
    /// <para>
    /// <b>This used to register far less than its three-argument sibling.</b> Everything a component
    /// injects is built from <c>TeamManagementService&lt;TMember&gt;</c>, so with no member type none of
    /// it was registered, and the first sign was a failure while rendering a page. It now resolves one,
    /// and <c>TeamServiceCompletenessCheck</c> says so at startup if it still cannot.
    /// </para>
    /// </remarks>
    public void RegisterTeamService<TServiceBase, TUserService>()
        where TServiceBase : TeamServiceBase
        where TUserService : UserServiceBase
    {
        _teamService = typeof(TServiceBase);
        _userService = typeof(TUserService);
        _memberType = TeamMemberTypeResolver.Resolve(typeof(TServiceBase));
    }

    /// <summary>
    /// Registers the host's team and user services with an <b>explicit member type</b> — use this when
    /// your member carries properties of your own.
    /// </summary>
    /// <remarks>
    /// An explicit <typeparamref name="TMember"/> always wins over what the two-argument overload would
    /// infer: this records a decision, and inference only fills a gap where none was expressed.
    /// </remarks>
    public void RegisterTeamService<TServiceBase, TUserService, TMember>()
        where TServiceBase : TeamServiceBase
        where TUserService : UserServiceBase
        where TMember : class, ITeamMember
    {
        _teamService = typeof(TServiceBase);
        _userService = typeof(TUserService);
        _memberType = typeof(TMember);
    }

    public void RegisterApiKeyAdministrationService<TApiKeyService>()
        where TApiKeyService : IApiKeyAdministrationService
    {
        _apiKeyService = typeof(TApiKeyService);
    }

    /// <summary>
    /// Register a custom claims enricher that runs before team member lookup and consent evaluation.
    /// Use this to inject global roles or other claims from external sources (e.g. database).
    /// </summary>
    public void AddClaimsEnricher<TEnricher>()
        where TEnricher : class, ITeamClaimsEnricher
    {
        _claimsEnricher = typeof(TEnricher);
    }

    /// <summary>
    /// Register a custom <see cref="IThargaTextProvider"/> to localize Tharga.Team UI strings (e.g. the
    /// profile menu and team selector). Overrides the built-in English default; when none is registered,
    /// the English defaults are used.
    /// </summary>
    public void AddTextProvider<TProvider>()
        where TProvider : class, IThargaTextProvider
    {
        _textProvider = typeof(TProvider);
    }
}