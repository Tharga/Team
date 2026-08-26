using Radzen;
using Microsoft.AspNetCore.Authentication.Cookies;
using Serilog;
using Serilog.Events;
using Tharga.Mcp;
using Tharga.MongoDB;
using Tharga.Team.Blazor.Features.Simulation;
using Tharga.Team.Mcp;
using Tharga.Team.Sample.Components;
using Tharga.Team.Sample.Framework;
using Tharga.Team.Sample.Framework.Team;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Entra;
using Tharga.Team.Images;
using Tharga.Team.MongoDB;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support;
using Tharga.Team.Support.Cases;

var builder = WebApplication.CreateBuilder(args);

// Logs land in <project>/logs so a failed circuit can be traced after the fact — the browser only shows
// the error boundary, and the terminal scrolls away. ContentRootPath (not the working directory) keeps
// them next to the source whether the app is started from the IDE or `dotnet run`.
builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(builder.Environment.ContentRootPath, "logs", "sample-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        restrictedToMinimumLevel: LogEventLevel.Information,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}"));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddRadzenComponents();
builder.Services.AddRadzenCookieThemeService();

builder.AddThargaTeam(o =>
{
    o.Blazor.Title = "Tharga Team Sample";
    o.Blazor.RegisterTeamService<TeamService, UserService, TeamMember>();
    o.Blazor.AutoCreateFirstTeam = false;
    o.Blazor.AllowTeamCreation = true;
    o.Blazor.AddClaimsEnricher<DeveloperRoleEnricher>();
    o.Blazor.Consent.ShowToggle = true;

    // Let a team owner/administrator view the app as a less privileged member, to check scope-gated UI
    // without keeping throwaway users. De-escalation only: the effective set is always a subset of what
    // the caller really holds. Off by default; the sample turns it on so the path is exercised.
    o.Blazor.Simulation.Enabled = true;

    // The sample mounts UserProfileView at /account rather than the toolkit's default /profile, so this
    // option is what keeps the profile menu's User item pointing at it. Without it that item navigates to
    // a literal "profile" and 404s — the defect these options were added for. Leaving both unset keeps the
    // built-in routes, so an ordinary host needs neither.
    o.Blazor.ProfilePath = "/account";
    // o.Blazor.TeamPath   = "/team";   // unset: the sample does mount TeamComponent at the default route.

    // Demo: revalidate team claims every 20 seconds so a member removal, access downgrade, or consent
    // change is reflected quickly in a live circuit while testing (#127). The production default is a slow
    // 30 minutes; set o.Blazor.ClaimRevalidation.Enabled = false to turn revalidation off entirely.
    o.Blazor.ClaimRevalidation.Interval = TimeSpan.FromSeconds(20);

    // Cross-team visibility: grant the consent roles the teams:read system scope, so a Developer sees
    // every team (not just their own) with a badge showing what each has consented to. Discovery only —
    // access inside a team still depends on that team's consent.
    o.Blazor.Consent.Roles = ["Developer"];
    o.Blazor.Consent.GrantTeamsRead = true;

    // Demo: localize the team menu strings (here to Swedish). A real app would bridge to its content system.
    o.Blazor.AddTextProvider<SampleMenuTextProvider>();

    // Advanced mode unlocks the full API key UI (access level, roles, scope overrides, tags).
    o.ApiKey.AdvancedMode = true;

    // Register scopes so the scope-override picker and Custom (no-base-scope) keys have something to grant.
    o.ConfigureScopes = scopes =>
    {
        // A spread across access levels so option-(b) pickers show a mix of inherited (disabled) and addable
        // scopes. Remember: Owner/Administrator inherit ALL scopes — to see addable ones, test against a
        // lower-level member or an AccessLevel.Custom key.
        scopes.Register("orders:read", AccessLevel.Viewer, "View orders and order details.");
        scopes.Register("orders:write", AccessLevel.User, "Create and edit orders.");
        scopes.Register("orders:refund", AccessLevel.Administrator, "Issue refunds on orders.");
        scopes.Register("valuegroup:read", AccessLevel.Viewer, "Read value groups.");
        scopes.Register("content:load", AccessLevel.Viewer, "Load published content.");
        scopes.Register("content:publish", AccessLevel.User, "Publish content to live.");
        scopes.Register("pim:manage", AccessLevel.Administrator, "Manage the product information catalog.");
        scopes.Register("firewall:open", AccessLevel.Administrator); // no description — shows no tooltip
        scopes.Register("reports:export", AccessLevel.User, "Export reports to file.");
        scopes.Register("billing:manage", AccessLevel.Administrator, "Manage billing and invoices.");

        // Grant-only: reaches no member by access level, not even Owner or Administrator, and cannot be
        // added to a runtime custom role or a scope override. The only way to hold it is the CaseOfficer
        // role registered below. Sign in as an owner and open /scopes to see it locked and ungranted.
        scopes.RegisterGrantOnly("case:read", "Read secrecy-classified case records.");
    };

    // Demo tenant roles (bundles of scopes). Assign these to members; their scopes resolve live now that
    // the role->scope linkage is fixed.
    o.ConfigureTenantRoles = roles =>
    {
        roles.Register("Editor", ["orders:write", "content:publish", "reports:export"], "Content editors — manage orders and publish content.");
        roles.Register("Support", ["orders:read", "valuegroup:read"]); // no description — tooltip shows scopes only
        roles.Register("CaseOfficer", ["case:read"], "The only grant path for the grant-only case:read scope.");
    };

    // Demo: let team admins define their own custom roles at runtime (see the /roles page → TenantRoleManager).
    o.EnableDynamicRoles = true;

    // Demo system scopes (global capabilities for system API keys; separate from team scopes).
    o.ConfigureSystemScopes = scopes =>
    {
        scopes.Register(SystemTeamScopes.Read, "See every team (cross-team discovery).");
        scopes.Register("system:metrics:read", "Read infrastructure metrics.");
        // mcp:discover is registered by mcp.AddTeam() — in both registries, so it is grantable by access
        // level, by system role, or to a system API key. Registering it here as well is harmless (the
        // toolkit skips a name already present) but redundant.
    };

    // Map app/global roles to system scopes — a Developer user gains these as claims (team-independent).
    // Note teams:read is NOT listed here: Consent.GrantTeamsRead adds it on top of this mapping, which is
    // the composition case (Map would throw on an already-mapped role; the toolkit-side grant merges).
    o.ConfigureSystemRoles = roles =>
    {
        // Everything mapped here is granted **system-wide** and carries the SystemScope claim type, so it
        // never satisfies a check that asks for a scope on a specific team.
        //
        // audit:read appears here *and* is registered as a team scope at Administrator level. That is not a
        // conflict: the system grant opens the cross-team /audit view, while a team administrator's
        // access-level grant only opens their own team's log. Before the two claim types existed, the team
        // grant satisfied the cross-team gate — any team administrator could read the whole system's log.
        //
        // apikey:manage is deliberately absent. It belongs to a team administrator, earned through access
        // level; a Developer should not manage a team's keys merely for being a Developer.
        //
        // teams:delete opens the Delete action on the UsersView Teams tab. Unlike teams:read above, no
        // consent option grants it — deleting a team is an operator capability, and a team consenting to
        // inbound access says nothing about who may destroy it. A host with an Administrator app role
        // grants it the same way: roles.Map("Administrator", SystemTeamScopes.Delete).
        //
        // simulation:demo is a system scope because demo mode drops system scopes and application roles --
        // it offers nothing to a caller holding none, which is every customer's own team owner. The run-as
        // half is the separate team scope simulation:use, granted by access level, so a team administrator
        // keeps "view as another user" without gaining demo mode (Tharga/Team#223). Without this line the
        // sample would enable simulation and then have no way to reach demo mode at all.
        roles.Map("Developer", "system:metrics:read", "mcp:discover", ApiKeyScopes.SystemManage, AuditScopes.Read, SystemUserScopes.Manage, SystemTeamScopes.Delete, SimulationScopes.Demo);
    };

    // Controllers on, which exposes the toolkit's own REST endpoints — GET /api/audit — plus Swagger at
    // /swagger. The endpoints accept an API key by default; add a cookie scheme to reach them signed in.
    o.Controllers = new ThargaControllerOptions { SwaggerTitle = "Tharga Team Sample API" };
    o.Controllers.AuthenticationSchemes.Add(CookieAuthenticationDefaults.AuthenticationScheme);

    // Logger | MongoDB so the audit entries are both logged and queryable by AuditLogView — the default
    // is Logger-only, which leaves the /audit page empty.
    o.Audit = new AuditOptions { StorageMode = AuditStorageMode.Logger | AuditStorageMode.MongoDB };
});

// Demo: attach host-defined metadata to every audit entry (visible in the /audit detail row).
builder.Services.AddThargaAuditEnricher<SampleAuditEnricher>();

// Demo: audit work that has no HTTP caller. Writes one entry ~5s after startup, attributed to the job
// rather than to a phantom user — visible on /audit as System / Background.
builder.Services.AddHostedService<SampleBackgroundJob>();

// Entra as the user directory (verify users, list directory-only users, opt-in directory delete).
// App-only Graph auth needs a client secret, so the directory (and its UI) only lights up when one is
// configured — e.g. `dotnet user-secrets set "AzureAd:ClientSecret" "<secret>"`. Graph permissions:
// User.Read.All (verify/list), User.ReadWrite.All (delete).
if (!string.IsNullOrEmpty(builder.Configuration["AzureAd:ClientSecret"]))
{
    builder.Services.AddThargaEntraUserDirectory(builder.Configuration);
}

builder.Services.AddThargaMcp(mcp =>
{
    // RequireAuth is on by default and now accepts an API key: AddTeam() contributes the API-key scheme,
    // so the endpoint no longer falls back to the application's default (OIDC) and reject agents.
    mcp.AddTeam();
});

// TeamAccessInterceptor is deliberately NOT registered yet. Claim construction reads the user record to
// decide what the caller may do, so it necessarily precedes any authorization decision — and every
// self-service operation passes through its decorator without one either. Both are legitimately
// unauthorized, so the guard rejected them and took the site down. See the notes in TeamAccessInterceptor.
builder.AddMongoDB();

builder.Services.AddScoped<AppUserAdminService>();

// Auto-downscale uploaded icons larger than IconOptions.MaxDimension (256px) instead of rejecting them.
builder.Services.AddThargaImageProcessing();

// Slack notifications for audited events. Registered with no secrets checked in, so it stays dormant
// unless Slack:BotToken and Slack:Channel are supplied — the point here is that the wiring resolves
// inside the real application graph. The container-validation test builds a bare collection, and a bare
// collection is exactly what missed the captive dependency that stopped this sample from starting.
builder.Services.AddThargaSupport(o =>
{
    o.Slack.BotToken = builder.Configuration["Slack:BotToken"];
    o.Notifications.DefaultChannel = builder.Configuration["Slack:Channel"];
});

builder.Services.AddThargaTeamRepository(o =>
{
    o.RegisterUserRepository<UserEntity>();
    o.RegisterTeamRepository<TeamEntity, TeamMember>();
});

// Support cases. Registered whether or not Slack is configured -- with no channel the cases are site-only,
// which is the ordinary shape for a host that never wanted Slack rather than a degraded one.
builder.Services.AddThargaSupportCases(o =>
{
    o.SlackChannel = builder.Configuration["Slack:SupportChannel"];
    o.SigningSecret = builder.Configuration["Slack:SigningSecret"];
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseSerilogRequestLogging();

app.UseHttpsRedirection();
app.UseAntiforgery();

app.UseThargaTeam();
app.UseThargaMcp();

// Where Slack posts thread replies. Public and unauthenticated by design -- Slack cannot present a
// credential, so the request signature is the credential, verified before the body is read.
app.MapThargaSupportSlack();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
