using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Blazor.Framework;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Blazor.Framework;

public static class ThargaBlazorRegistration
{
    /// <summary>
    /// Registers Tharga Team Blazor components on a host application builder, threading
    /// <see cref="IHostApplicationBuilder.Configuration"/> through automatically.
    /// </summary>
    public static void AddThargaTeamBlazor(this IHostApplicationBuilder builder, Action<ThargaBlazorOptions> options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddThargaTeamBlazor(options, builder.Configuration);
    }

    public static void AddThargaTeamBlazor(this IServiceCollection services, Action<ThargaBlazorOptions> options = null, IConfiguration configuration = null)
    {
        var o = new ThargaBlazorOptions();
        options?.Invoke(o);

        services.AddThargaBlazor(bo => bo.Title = o.Title, configuration);

        // UI string provider — a consumer-supplied provider (via AddTextProvider) localizes the strings;
        // otherwise the built-in default returns English.
        if (o._textProvider != null)
        {
            services.AddScoped(typeof(IThargaTextProvider), o._textProvider);
        }
        services.TryAddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();

        // Where the claims path keeps its lookups. TryAdd, so a host running more than one instance registers
        // a shared implementation and wins -- the built-in is process-local and cannot see another instance's
        // writes. Singleton because the services reading it are scoped: a scoped cache would live for one
        // request and cache nothing across the requests it exists to serve.
        services.TryAddSingleton<ITeamCache, InMemoryTeamCache>();

        if (o._teamService != null)
        {
            services.AddScoped<ITeamStateService, TeamStateService>();

            // Scoped, like every other Blazor-side service here. It re-resolves the caller's real grant
            // per request rather than reading the (possibly reduced) principal, so it must not be a
            // singleton capturing anything.
            services.AddScoped<Features.Simulation.AccessSimulationState>();

            // Only when the host turned it on: without simulation there is nothing to record, and an
            // enricher that reads a cookie on every audit entry should not exist for a feature nobody
            // enabled.
            if (o.Simulation.Enabled)
            {
                services.AddSingleton<Tharga.Team.Service.Audit.IAuditEnricher, Features.Simulation.AccessSimulationAuditEnricher>();

                // Reaches the singleton enricher from a circuit, where there is no HttpContext to read.
                services.AddSingleton<Features.Simulation.AccessSimulationPrincipalAccessor>();
                services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, Features.Simulation.AccessSimulationCircuitHandler>();
            }

            services.AddScoped(o._teamService);
            services.AddScoped(typeof(ITeamService), sp => sp.GetRequiredService(o._teamService));

            services.AddScoped(o._userService);
            services.AddScoped(typeof(IUserService), sp =>
            {
                var userService = sp.GetRequiredService(o._userService);

                // A first sign-in creates the user record while resolving the caller, outside any service the
                // auditing decorators wrap -- so it is subscribed per scope here rather than decorated.
                // Failing to audit must never fail the sign-in that triggered it (Tharga/Team#142).
                if (userService is UserServiceBase auditable)
                {
                    var auditLogger = sp.GetService<CompositeAuditLogger>();
                    var logger = sp.GetService<ILogger<UserServiceCompletenessCheck>>();

                    if (auditLogger != null)
                    {
                        auditable.UserCreatedEvent += (_, e) =>
                        {
                            try
                            {
                                auditLogger.Log(AuthAuditEntries.UserCreated(e.User, e.Principal));
                            }
                            catch (Exception ex)
                            {
                                logger?.LogWarning(ex, "Failed to audit the creation of a user on first sign-in.");
                            }
                        };
                    }
                }

                return userService;
            });

            // Runs at startup, once everything else is registered — reachability depends on whether an
            // icon store and a directory are present, and those may be registered after this point.
            var userServiceType = o._userService;
            var throwOnIncomplete = o.ThrowOnIncompleteUserService;
            services.AddSingleton<IHostedService>(sp => new UserServiceCompletenessCheck(
                sp, userServiceType, throwOnIncomplete,
                sp.GetService<ILogger<UserServiceCompletenessCheck>>()));

            if (o._memberType != null)
            {
                var managementServiceType = typeof(TeamManagementService<>).MakeGenericType(o._memberType);
                services.AddScoped(managementServiceType);

                // TryAdd, so a host substituting or decorating one facet keeps its own. That is the one
                // legitimate reason this was ever left to the host, and it is answered here rather than
                // by making every host wire all five. Registering facets a host never resolves costs
                // nothing -- they are scoped and simply never constructed.
                foreach (var facet in TeamServiceFacets.All)
                    services.TryAddScoped(facet, sp => sp.GetRequiredService(managementServiceType));
            }

            // Reports at startup if any facet is still unresolvable, naming it. Without this the first
            // sign is a component failing to render -- which for a page nobody opens until production is
            // the worst place to find out.
            var teamServiceType = o._teamService;
            var throwOnIncompleteTeam = o.ThrowOnIncompleteTeamService;
            services.AddSingleton<IHostedService>(sp => new TeamServiceCompletenessCheck(
                sp, teamServiceType, throwOnIncompleteTeam,
                sp.GetService<ILogger<TeamServiceCompletenessCheck>>()));

            // A custom ITeamCache that the host's own services never received is the one way this seam can be
            // configured and silently not take effect -- and what it silently fails to do is keep authorization
            // fresh across instances. Registered unconditionally; it no-ops unless a custom cache is present.
            services.AddSingleton<IHostedService>(sp => new TeamCacheWiringCheck(
                sp, teamServiceType, userServiceType));

            if (o._apiKeyService != null)
            {
                services.AddAuditedApiKeyAdministrationService(o._apiKeyService);
            }

            // Register default team and API key scopes unless already registered
            if (!services.Any(d => d.ServiceType == typeof(IScopeRegistry)))
            {
                services.AddThargaScopes(scopes =>
                {
                    scopes.Register(TeamScopes.Read, AccessLevel.Viewer, "View team details and members.");
                    scopes.Register(TeamScopes.Manage, AccessLevel.Administrator, "Administer the team: rename, delete, and transfer ownership.");
                    scopes.Register(TeamScopes.MemberManage, AccessLevel.Administrator, "Manage team members — invite, remove, edit display names, and change access level, roles, and scope overrides.");
                    scopes.Register(ApiKeyScopes.Manage, AccessLevel.Administrator, "Create, refresh, lock, and delete API keys.");
                    scopes.Register(AuditScopes.Read, AccessLevel.Administrator, "View the audit log.");
                    // Administrator level, which yields "team owner or administrator" without naming
                    // them: those levels are granted every registered scope. A host can widen it to a
                    // tenant role, or withhold it, without a toolkit change.
                    scopes.Register(Features.Simulation.SimulationScopes.Simulate, AccessLevel.Administrator, "View the application as a less privileged user (de-escalation only).");
                });
            }

            // Built-in system scopes: teams:delete authorizes deleting any team (cross-team), users:manage
            // authorizes user administration. Merge-safe with any consumer ConfigureSystemScopes; grant
            // them via ConfigureSystemRoles or a system API key.
            services.AddThargaSystemScopes(scopes =>
            {
                if (scopes.All.All(s => s.Name != SystemTeamScopes.Delete))
                    scopes.Register(SystemTeamScopes.Delete, "Delete any team (cross-team), regardless of membership or the AllowTeamCreation option.");
                    scopes.Register(SystemTeamScopes.Purge, "Permanently remove a soft-deleted team and drop its storage. Irreversible, and the only team operation needing the database privilege to drop data.");
                if (scopes.All.All(s => s.Name != SystemUserScopes.Manage))
                    scopes.Register(SystemUserScopes.Manage, "Administer users (cross-team): verify against the external directory, list directory-only users, and delete users.");
                if (scopes.All.All(s => s.Name != SystemTeamScopes.Manage))
                    scopes.Register(SystemTeamScopes.Manage, "Rename any team and set its icon (cross-team). Does not grant consent or custom-role changes.");
                if (scopes.All.All(s => s.Name != SystemTeamScopes.AssignOwner))
                    scopes.Register(SystemTeamScopes.AssignOwner, "Give an ownerless team an owner, chosen from its existing members. Refused when the team already has one.");
            });

            services.AddScoped<IUserManagementService>(sp => new UserManagementService(
                sp.GetRequiredService<IUserService>(),
                sp.GetRequiredService<ITeamService>(),
                sp.GetService<IUserDirectoryService>(),
                o.WriteNameToDirectory));

            // Server-side claims enrichment — always registered, reads selected_team_id cookie.
            // The membership/consent claim computation is shared with the in-circuit revalidator below.
            services.AddHttpContextAccessor();
            services.TryAddScoped<TeamMembershipClaimsBuilder>();
            services.AddTransient<IClaimsTransformation, TeamServerClaimsTransformation>();

            // Make scope/access-level proxies resolve the caller from the circuit too (not just HttpContext),
            // so [RequireScope]/[RequireAccessLevel] enforce in interactive Blazor Server as well as on the API.
            services.Replace(ServiceDescriptor.Scoped<ITeamPrincipalAccessor, BlazorTeamPrincipalAccessor>());

            // Custom claims enricher — runs before member lookup and consent evaluation
            if (o._claimsEnricher != null)
            {
                services.AddScoped(typeof(ITeamClaimsEnricher), o._claimsEnricher);
            }

            if (!o.SkipAuthStateDecoration)
            {
                // Client-side (WASM) claims enrichment via JS interop / LocalStorage.
                // Only needed for pure WASM apps. Server/SSR apps use the transformation above.
                var existing = services.LastOrDefault(d => d.ServiceType == typeof(AuthenticationStateProvider));
                if (existing != null)
                {
                    services.Remove(existing);

                    if (existing.ImplementationType != null)
                    {
                        services.AddKeyedScoped(typeof(AuthenticationStateProvider), "inner-auth-state", existing.ImplementationType);
                    }
                    else if (existing.ImplementationFactory != null)
                    {
                        var factory = existing.ImplementationFactory;
                        services.AddKeyedScoped("inner-auth-state", (sp, _) => (AuthenticationStateProvider)factory(sp));
                    }
                }

                services.AddScoped<AuthenticationStateProvider, TeamClaimsAuthenticationStateProvider>();
            }

            // Periodic team-claim revalidation for live Blazor Server circuits (#127): re-evaluate
            // membership/access/consent on an interval and refresh the principal in place, so removals,
            // downgrades, and consent revocations stop being frozen for the life of the circuit. Server/SSR
            // only — SkipAuthStateDecoration (true for Server/SSR) is the existing hosting signal; a WASM
            // client has no server circuit to revalidate this way. The provider becomes the
            // ServerAuthenticationStateProvider (seeded via IHostEnvironmentAuthenticationStateProvider), so
            // both the UI and BlazorTeamPrincipalAccessor observe the refreshed claims. Only wrap the
            // framework's ServerAuthenticationStateProvider — never clobber a consumer- or test-supplied
            // provider (which would break its seeding).
            var existingAuthProvider = services.LastOrDefault(d => d.ServiceType == typeof(AuthenticationStateProvider));
            var wrapsServerProvider = existingAuthProvider?.ImplementationType != null
                && typeof(ServerAuthenticationStateProvider).IsAssignableFrom(existingAuthProvider.ImplementationType);
            if (o.ClaimRevalidation.Enabled && o.SkipAuthStateDecoration && wrapsServerProvider)
            {
                services.TryAddScoped<TeamClaimRevalidator>();
                services.AddScoped<TeamRevalidatingAuthenticationStateProvider>();
                services.Replace(ServiceDescriptor.Scoped<AuthenticationStateProvider>(
                    sp => sp.GetRequiredService<TeamRevalidatingAuthenticationStateProvider>()));
                services.Replace(ServiceDescriptor.Scoped<IHostEnvironmentAuthenticationStateProvider>(
                    sp => sp.GetRequiredService<TeamRevalidatingAuthenticationStateProvider>()));
            }
        }

        // Audit decorator for ITeamService — wrap when audit logging is configured. Uses deferred
        // resolution so AddThargaAuditLogging() can be called after AddThargaTeamBlazor().
        // (IApiKeyAdministrationService audit is owned by AddAuditedApiKeyAdministrationService, applied
        //  at resolve time, so it is order-independent and not clobbered by AddThargaApiKeyAuthentication.)
        if (o._teamService != null)
        {
            DecorateWithAudit<ITeamService>(services,
                (inner, logger, http) => new AuditingTeamServiceDecorator(inner, logger, http));

            // Service-layer authorization — outermost (checks before audit/operation), so the same scope
            // rules protect the Blazor circuit and any consumer's REST controller calling ITeamService.
            services.TryAddScoped<TeamAuthorizer>();
            DecorateWithAuthorization(services, new TeamLifecycleOptions { AllowTeamCreation = o.AllowTeamCreation });

            // User management gets the same treatment: audit inside, authorization outermost.
            DecorateWithAudit<IUserManagementService>(services,
                (inner, logger, http) => new AuditingUserManagementServiceDecorator(inner, logger, http));
            DecorateUserManagementWithAuthorization(services);

            // The user store itself: cross-user reads/writes require users:manage; self-service passes.
            DecorateUserServiceWithAuthorization(services);
        }

        RegisterIcons(services, o);
        RegisterEmail(services, o);

        services.AddSingleton(Options.Create(o));
    }

    /// <summary>
    /// Icons — two seams with built-in defaults. Storage: a custom <see cref="IIconStore"/> wins over the
    /// built-in <c>MongoIconStore</c> (registered by <c>AddThargaTeamRepository</c>). Sourcing:
    /// <see cref="StoredIconSource"/> is registered FIRST so a stored icon takes precedence, then consumer
    /// sources fill in, then the fallbacks.
    /// </summary>
    /// <remarks>
    /// Registered here rather than in the <c>AddThargaTeam</c> facade, and unconditionally. <c>LoginDisplay</c>
    /// sits in the layout and hard-injects <see cref="Features.User.AvatarChangeNotifier"/>, so a host on the
    /// documented granular path used to get <c>InvalidOperationException</c> on every render, taking the
    /// circuit with it (Tharga/Team#157). An opt-in would have reproduced the same crash for anyone who
    /// forgot it, and <c>ValidateOnBuild</c> cannot warn them — Blazor resolves <c>@inject</c> properties at
    /// render time, outside the graph the validator walks.
    /// </remarks>
    private static void RegisterIcons(IServiceCollection services, ThargaBlazorOptions o)
    {
        // Every property, not a named list. Copying only MaxBytes and AllowedContentTypes meant
        // o.Icon.MaxUploadBytes and o.Icon.MaxDimension compiled, read naturally and did nothing
        // (Tharga/Team#177) -- and MaxUploadBytes is the one a consumer most often needs to raise, because with
        // an image processor registered it is the only thing standing between a phone photo and a successful
        // upload.
        services.Configure<IconOptions>(io => OptionsForwarder.Copy(o.Icon, io));

        if (o._iconStoreType != null)
        {
            services.AddScoped(typeof(IIconStore), o._iconStoreType);
        }

        services.AddSingleton(o.IconSettings);
        services.AddScoped<IIconSource, StoredIconSource>();
        foreach (var sourceType in o._iconSourceTypes)
        {
            services.AddScoped(typeof(IIconSource), sourceType);
        }

        // Fallbacks for users with no uploaded/custom icon (an upload thus overrides them): Gravatar (if
        // enabled), then a configured generic default image, then the avatar's own initials.
        services.AddScoped<IIconSource, GravatarIconSource>();
        services.AddScoped<IIconSource, DefaultIconSource>();
        services.AddScoped<IIconResolver, IconResolver>();
        services.AddScoped<Features.User.AvatarChangeNotifier>();
        services.AddHttpClient(IconHttpClientName);
    }

    /// <summary>
    /// The email sender, as a three-way choice: a custom implementation wins, then SMTP if
    /// <see cref="ThargaBlazorOptions.Email"/> is set, then nothing.
    /// </summary>
    /// <remarks>
    /// Registered here rather than only in the <c>AddThargaTeam</c> facade (Tharga/Team#176). It used to
    /// exist only there, so a granular host had to reproduce it by hand against internal knowledge of what
    /// the facade does — and it failed more quietly than the icon equivalent (#157), because
    /// <c>InviteUserDialog</c> and <c>TeamComponent</c> both resolve the sender with <c>GetService</c> and
    /// degrade to manual link copying. A granular host got no error; invitations simply were never sent, and
    /// the fallback looked like intended behaviour.
    /// <para>
    /// Nothing is registered when neither is configured, so <c>GetService&lt;ITeamEmailSender&gt;()</c>
    /// returning null keeps meaning "no email configured" rather than "wiring forgotten".
    /// </para>
    /// </remarks>
    private static void RegisterEmail(IServiceCollection services, ThargaBlazorOptions o)
    {
        if (o._emailSenderType != null)
        {
            services.AddScoped(typeof(ITeamEmailSender), o._emailSenderType);
            return;
        }

        if (o.Email == null) return;

        // Copied whole rather than property-by-property: a named list is what dropped two IconOptions
        // properties on this same path (Tharga/Team#177). FromName is assigned after the copy because it
        // alone has a fallback -- the application title, so an unconfigured sender name is still meaningful.
        var fromName = o.Email.FromName ?? o.Title;
        services.Configure<EmailOptions>(eo =>
        {
            OptionsForwarder.Copy(o.Email, eo);
            eo.FromName = fromName;
        });

        services.AddScoped<ITeamEmailSender, SmtpTeamEmailSender>();
    }

    /// <summary>Named client used to fetch remote icon images (e.g. Gravatar).</summary>
    internal const string IconHttpClientName = "tharga-icon-download";

    /// <summary>
    /// Maps the middleware and endpoints the Blazor layer needs — currently the icon-serving endpoint at
    /// <see cref="IconRoute.Base"/>/{reference}. Call this once on the built application when using the
    /// granular setup path; <c>UseThargaTeam</c> calls it for you.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="AddThargaTeamBlazor(IHostApplicationBuilder, Action{ThargaBlazorOptions})"/>,
    /// mirroring the existing <c>AddThargaAuth</c> / <c>UseThargaAuth</c> pair. Without it a granular host
    /// could register the icon chain and still not serve a stored icon back, because the endpoint was
    /// mapped only by the facade (Tharga/Team#157).
    /// </remarks>
    public static void UseThargaTeamBlazor(this WebApplication app)
    {
        app.MapGet($"{IconRoute.Base}/{{reference}}", async (string reference, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (context.User?.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();

            var store = context.RequestServices.GetService<IIconStore>();
            if (store == null)
                return Results.NotFound();

            var content = await store.LoadAsync(reference, cancellationToken);
            if (content == null)
                return Results.NotFound();

            context.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            return Results.File(content.Data, content.ContentType);
        });
    }

    private static void DecorateWithAuthorization(IServiceCollection services, TeamLifecycleOptions lifecycle)
    {
        var existing = services.LastOrDefault(d => d.ServiceType == typeof(ITeamService));
        if (existing == null) return;

        services.Remove(existing);

        services.AddScoped<ITeamService>(sp =>
        {
            ITeamService inner;
            if (existing.ImplementationFactory != null)
                inner = (ITeamService)existing.ImplementationFactory(sp);
            else if (existing.ImplementationType != null)
                inner = (ITeamService)ActivatorUtilities.CreateInstance(sp, existing.ImplementationType);
            else
                throw new InvalidOperationException("Cannot resolve inner ITeamService for authorization decoration.");

            var authorizer = sp.GetRequiredService<TeamAuthorizer>();
            var scopeRegistry = sp.GetService<IScopeRegistry>();
            var tenantRoleRegistry = sp.GetService<ITenantRoleRegistry>();
            var dynamicRoleOptions = sp.GetService<DynamicTenantRoleOptions>();
            return new AuthorizationTeamServiceDecorator(inner, authorizer, lifecycle, scopeRegistry, tenantRoleRegistry, dynamicRoleOptions?.ManageScope);
        });
    }

    private static void DecorateUserServiceWithAuthorization(IServiceCollection services)
    {
        var existing = services.LastOrDefault(d => d.ServiceType == typeof(IUserService));
        if (existing == null) return;

        services.Remove(existing);

        services.AddScoped<IUserService>(sp =>
        {
            IUserService inner;
            if (existing.ImplementationFactory != null)
                inner = (IUserService)existing.ImplementationFactory(sp);
            else if (existing.ImplementationType != null)
                inner = (IUserService)ActivatorUtilities.CreateInstance(sp, existing.ImplementationType);
            else
                throw new InvalidOperationException("Cannot resolve inner IUserService for authorization decoration.");

            // Cache invalidation sits closest to the store, so it runs on the write that actually
            // happened — after authorization has already decided the call may proceed.
            var cacheInvalidating = new CacheInvalidatingUserServiceDecorator(inner);

            return new AuthorizationUserServiceDecorator(cacheInvalidating, sp.GetRequiredService<TeamAuthorizer>(), sp.GetRequiredService<ITeamService>);
        });
    }

    private static void DecorateUserManagementWithAuthorization(IServiceCollection services)
    {
        var existing = services.LastOrDefault(d => d.ServiceType == typeof(IUserManagementService));
        if (existing == null) return;

        services.Remove(existing);

        services.AddScoped<IUserManagementService>(sp =>
        {
            IUserManagementService inner;
            if (existing.ImplementationFactory != null)
                inner = (IUserManagementService)existing.ImplementationFactory(sp);
            else if (existing.ImplementationType != null)
                inner = (IUserManagementService)ActivatorUtilities.CreateInstance(sp, existing.ImplementationType);
            else
                throw new InvalidOperationException("Cannot resolve inner IUserManagementService for authorization decoration.");

            return new AuthorizationUserManagementServiceDecorator(inner, sp.GetRequiredService<TeamAuthorizer>());
        });
    }

    private static void DecorateWithAudit<TService>(
        IServiceCollection services,
        Func<TService, CompositeAuditLogger, IHttpContextAccessor, TService> factory)
        where TService : class
    {
        var existing = services.LastOrDefault(d => d.ServiceType == typeof(TService));
        if (existing == null) return;

        services.Remove(existing);

        services.AddScoped(sp =>
        {
            // Resolve the inner service from the original registration
            TService inner;
            if (existing.ImplementationFactory != null)
                inner = (TService)existing.ImplementationFactory(sp);
            else if (existing.ImplementationType != null)
                inner = (TService)ActivatorUtilities.CreateInstance(sp, existing.ImplementationType);
            else
                throw new InvalidOperationException($"Cannot resolve inner {typeof(TService).Name} — no factory or type.");

            var auditLogger = sp.GetService<CompositeAuditLogger>();
            if (auditLogger == null) return inner;

            var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
            return factory(inner, auditLogger, httpContextAccessor);
        });
    }
}
