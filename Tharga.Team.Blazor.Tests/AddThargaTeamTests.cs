using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Blazor.Features.BreadCrumbs;
using Tharga.Blazor.Framework;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Tests;

public class AddThargaTeamTests
{
    private const string ValidAzureAdConfig = """
        {
            "AzureAd": {
                "Authority": "https://test.ciamlogin.com/test",
                "ClientId": "test-client-id",
                "TenantId": "test-tenant-id",
                "CallbackPath": "/signin-oidc"
            }
        }
        """;

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ValidAzureAdConfig));
        builder.Configuration.AddJsonStream(stream);
        return builder;
    }

    [Fact]
    public void RegistersAuthenticationServices()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam();
        var provider = builder.Services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IAuthenticationService>());
    }

    [Fact]
    public void RegistersBreadCrumbService()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam();
        var provider = builder.Services.BuildServiceProvider();

        Assert.Contains(builder.Services, d => d.ServiceType == typeof(BreadCrumbService));
    }

    [Fact]
    public void EnableDynamicRoles_True_RegistersTenantRoleService()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o => o.EnableDynamicRoles = true);

        Assert.Contains(builder.Services, d => d.ServiceType == typeof(ITenantRoleService));
    }

    [Fact]
    public void EnableDynamicRoles_Default_DoesNotRegisterTenantRoleService()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam();

        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(ITenantRoleService));
    }

    [Fact]
    public void EnableDynamicRoles_DefaultManageScope_IsTeamManage()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o => o.EnableDynamicRoles = true);
        var provider = builder.Services.BuildServiceProvider();

        var options = provider.GetService<DynamicTenantRoleOptions>();
        Assert.NotNull(options);
        Assert.Equal(TeamScopes.Manage, options.ManageScope);
    }

    [Fact]
    public void DynamicRoleManageScope_FlowsThroughFacade()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o =>
        {
            o.EnableDynamicRoles = true;
            o.DynamicRoleManageScope = "access:manage";
        });
        var provider = builder.Services.BuildServiceProvider();

        Assert.Equal("access:manage", provider.GetRequiredService<DynamicTenantRoleOptions>().ManageScope);
    }

    [Fact]
    public void AddThargaDynamicTenantRoles_ConfiguresManageScope()
    {
        var services = new ServiceCollection();
        services.AddThargaDynamicTenantRoles(o => o.ManageScope = "access:manage");
        var provider = services.BuildServiceProvider();

        Assert.Equal("access:manage", provider.GetRequiredService<DynamicTenantRoleOptions>().ManageScope);
        Assert.Contains(services, d => d.ServiceType == typeof(ITenantRoleService));
    }

    [Fact]
    public void AddThargaDynamicTenantRoles_DefaultsToTeamManage()
    {
        var services = new ServiceCollection();
        services.AddThargaDynamicTenantRoles();
        var provider = services.BuildServiceProvider();

        Assert.Equal(TeamScopes.Manage, provider.GetRequiredService<DynamicTenantRoleOptions>().ManageScope);
    }

    [Fact]
    public void RegistersBlazorOptions()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o => o.Blazor.Title = "Test App");
        var provider = builder.Services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<BlazorOptions>>();
        Assert.Equal("Test App", options.Value.Title);
    }

    [Fact]
    public void RegistersApiKeyServices()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam();

        Assert.Contains(builder.Services, d => d.ServiceType == typeof(IApiKeyRepository));
        Assert.Contains(builder.Services, d => d.ServiceType == typeof(IApiKeyRepositoryCollection));
    }

    private sealed class HostTextProvider : IThargaTextProvider
    {
        public Task<string> GetAsync(TextKey key) => Task.FromResult(key.Default);
    }

    /// <summary>The sample's own <c>o.Blazor.AddTextProvider</c> was dropped, and the default provider was registered instead.</summary>
    [Fact]
    public void ABlazorTextProvider_SetThroughAddThargaTeam_IsTheOneRegistered()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o => o.Blazor.AddTextProvider<HostTextProvider>());

        var registered = builder.Services.Where(d => d.ServiceType == typeof(IThargaTextProvider)).ToArray();

        Assert.Contains(registered, d => d.ImplementationType == typeof(HostTextProvider));
    }

    [Fact]
    public void ABlazorMenuItem_AddedThroughAddThargaTeam_ReachesTheLoginMenu()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o => o.Blazor.AddMenuItem("help", "myapp.menu.help", "Help", "/help"));
        var provider = builder.Services.BuildServiceProvider();

        var items = provider.GetRequiredService<IOptions<ThargaBlazorOptions>>().Value._menuItems;

        var item = Assert.Single(items);
        Assert.Equal("/help", item.Href);
    }

    [Fact]
    public void ABlazorIconSource_AddedThroughAddThargaTeam_IsKept()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o => o.Blazor.AddIconSource<HostIconSource>());
        var provider = builder.Services.BuildServiceProvider();

        Assert.Contains(typeof(HostIconSource), provider.GetRequiredService<IOptions<ThargaBlazorOptions>>().Value._iconSourceTypes);
    }

    private sealed class HostIconSource : IIconSource
    {
        public Task<IconImage> ResolveAsync(IconSubject subject, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>
    /// <b>The guard the issue asks for.</b> Every non-public registration field of the Blazor options must reach the layer
    /// that consumes it, so one added later cannot be missed again: each is set on the facade's options and found on the
    /// copy.
    /// </summary>
    [Fact]
    public void EveryRegistrationFieldOfTheBlazorOptions_IsForwardedToTheLayerThatConsumesIt()
    {
        var fields = ThargaBlazorOptionsForwarder.RegistrationFields().ToArray();
        Assert.NotEmpty(fields);

        foreach (var field in fields)
        {
            var from = new ThargaBlazorOptions();
            var to = new ThargaBlazorOptions();

            if (field.FieldType == typeof(Type))
            {
                field.SetValue(from, typeof(HostSentinel));

                ThargaBlazorOptionsForwarder.CopyRegistrations(from, to);

                Assert.True(typeof(HostSentinel) == (Type)field.GetValue(to), $"{field.Name} was not forwarded.");
            }
            else if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var element = field.FieldType.GetGenericArguments()[0];
                ((System.Collections.IList)field.GetValue(from)).Add(element == typeof(Type) ? typeof(HostSentinel) : System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(element));

                ThargaBlazorOptionsForwarder.CopyRegistrations(from, to);

                Assert.True(((System.Collections.IList)field.GetValue(to)).Count == 1, $"{field.Name} was not forwarded.");
            }
            else
            {
                throw new InvalidOperationException($"The field {field.Name} is of a kind this guard does not know ({field.FieldType.Name}); teach the forwarder and the guard about it.");
            }
        }
    }

    private sealed class HostSentinel;

    [Fact]
    public void TheAuditDefaultMode_SetThroughAddThargaTeam_IsForwarded()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o => o.Audit = new AuditOptions { DefaultAuditMode = AuditMode.None });
        var provider = builder.Services.BuildServiceProvider();

        Assert.Equal(AuditMode.None, provider.GetRequiredService<IOptions<AuditOptions>>().Value.DefaultAuditMode);
    }

    /// <summary>Every public setting of the audit options, not a list somebody has to extend: each is set to a non-default and must arrive.</summary>
    [Fact]
    public void EveryPublicAuditOption_SetThroughAddThargaTeam_IsForwarded()
    {
        var set = new AuditOptions
        {
            StorageMode = AuditStorageMode.Logger,
            CallerFilter = AuditCallerFilter.Api,
            EventFilter = AuditEventFilter.All,
            ExcludedActions = ["read"],
            DefaultAuditMode = AuditMode.None,
            ExcludedEndpoints = ["/health"],
            RetentionDays = 12,
            BatchSize = 7,
            FlushIntervalSeconds = 3
        };
        var builder = CreateBuilder();
        builder.AddThargaTeam(o => o.Audit = set);
        var got = builder.Services.BuildServiceProvider().GetRequiredService<IOptions<AuditOptions>>().Value;

        foreach (var property in typeof(AuditOptions).GetProperties().Where(p => p.CanRead && p.CanWrite))
            Assert.True(Equals(property.GetValue(set), property.GetValue(got)) || (property.GetValue(set) is Array a && a.Cast<object>().SequenceEqual(((Array)property.GetValue(got)).Cast<object>())),
                $"The audit option {property.Name} was not forwarded.");
    }

    [Fact]
    public void RegistersControllerServices()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam();

        Assert.Contains(builder.Services, d => d.ServiceType == typeof(ThargaControllerOptions));
    }

    [Fact]
    public void ControllersAuthenticationSchemes_AddedByTheHost_ReachTheControllerOptions()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o => o.Controllers.AuthenticationSchemes.Add("Cookies"));

        var controllerOptions = GetRegisteredControllerOptions(builder);

        Assert.Contains("Cookies", controllerOptions.AuthenticationSchemes);
        Assert.Contains(ApiKeyConstants.SchemeName, controllerOptions.AuthenticationSchemes);
        Assert.Equal(1, controllerOptions.AuthenticationSchemes.Count(x => x == ApiKeyConstants.SchemeName));
    }

    [Fact]
    public void ControllersAuthenticationSchemes_Untouched_StayTheApiKeyScheme()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam();

        Assert.Equal([ApiKeyConstants.SchemeName], GetRegisteredControllerOptions(builder).AuthenticationSchemes);
    }

    [Fact]
    public void ControllersAuthenticationSchemes_AreUsedByTheApiPolicy()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o => o.Controllers.AuthenticationSchemes.Add("Cookies"));

        using var provider = builder.Services.BuildServiceProvider();
        var policy = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.GetPolicy(ApiKeyConstants.ThargaApiPolicyName);

        Assert.NotNull(policy);
        Assert.Contains("Cookies", policy.AuthenticationSchemes);
    }

    [Fact]
    public void ControllersConfigureOpenApi_RunsAgainstTheManagedDocument()
    {
        var invoked = 0;
        var builder = CreateBuilder();
        builder.AddThargaTeam(o => o.Controllers.ConfigureOpenApi(_ => invoked++));

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<IOptionsMonitor<OpenApiOptions>>().Get("v1");

        Assert.Equal(1, invoked);
    }

    private static ThargaControllerOptions GetRegisteredControllerOptions(WebApplicationBuilder builder)
        => (ThargaControllerOptions)builder.Services.Single(d => d.ServiceType == typeof(ThargaControllerOptions)).ImplementationInstance;

    [Fact]
    public void SkipsControllers_WhenNull()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o => o.Controllers = null);

        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(ThargaControllerOptions));
    }

    [Fact]
    public void RegistersScopes_WhenConfigured()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o =>
        {
            o.ConfigureScopes = scopes => scopes.Register("test:read", AccessLevel.Viewer);
        });
        var provider = builder.Services.BuildServiceProvider();

        var registry = provider.GetService<IScopeRegistry>();
        Assert.NotNull(registry);
        Assert.Single(registry.All);
    }

    [Fact]
    public void SkipsScopes_WhenNotConfigured()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam();

        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(IScopeRegistry));
    }

    [Fact]
    public void RegistersTenantRoles_WhenConfigured()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o =>
        {
            o.ConfigureScopes = scopes => scopes.Register("test:read", AccessLevel.Viewer);
            o.ConfigureTenantRoles = roles => roles.Register("Editor", new[] { "test:read" });
        });
        var provider = builder.Services.BuildServiceProvider();

        var registry = provider.GetService<ITenantRoleRegistry>();
        Assert.NotNull(registry);
    }

    [Fact]
    public void SkipsTenantRoles_WhenNotConfigured()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam();

        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(ITenantRoleRegistry));
    }

    [Fact]
    public void RegistersAuditLogging_WhenConfigured()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o => o.Audit = new AuditOptions());

        Assert.Contains(builder.Services, d => d.ServiceType == typeof(CompositeAuditLogger));
    }

    [Fact]
    public void SkipsAuditLogging_WhenNotConfigured()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam();

        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(CompositeAuditLogger));
    }

    [Fact]
    public void ForwardsApiKeyOptions_To_ApiKeyOptions()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o =>
        {
            o.ApiKey.MinKeyLength = 40;
            o.ApiKey.MaxKeyLength = 48;
            o.ApiKey.MaxExpiryDays = 30;
            o.ApiKey.LastUsedThrottle = TimeSpan.FromMinutes(5);
            o.ApiKey.AutoLockKeys = true;
        });
        var provider = builder.Services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ApiKeyOptions>>().Value;
        Assert.Equal(40, options.MinKeyLength);
        Assert.Equal(48, options.MaxKeyLength);
        Assert.Equal(30, options.MaxExpiryDays);
        Assert.Equal(TimeSpan.FromMinutes(5), options.LastUsedThrottle);
        Assert.True(options.AutoLockKeys);
    }

    /// <summary>
    /// Every <see cref="ApiKeyOptions"/> setting reaches the container, driven by the type's own shape so
    /// a setting added later is covered without anyone remembering. The forwarding used to be a hand-written
    /// list, the defect class behind Tharga/Team#177; <c>OwnerRemovedAction</c> (#323) is the first setting
    /// added since it became reflection.
    /// </summary>
    [Fact]
    public void ForwardsEveryApiKeyOption()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o => o.ApiKey.OwnerRemovedAction = PrivateKeyOwnerRemovedAction.Disable);
        var provider = builder.Services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ApiKeyOptions>>().Value;
        Assert.Equal(PrivateKeyOwnerRemovedAction.Disable, options.OwnerRemovedAction);
        Assert.NotEmpty(OptionsForwarder.ForwardableProperties<ApiKeyOptions>());
        Assert.Contains(OptionsForwarder.ForwardableProperties<ApiKeyOptions>(), p => p.Name == nameof(ApiKeyOptions.OwnerRemovedAction));
    }

    [Fact]
    public void RegistersTheMemberRemovalCascade_WithThePrivateKeyParticipant()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam();

        Assert.Contains(builder.Services, d => d.ServiceType == typeof(TeamMemberRemovalCascade));
        Assert.Single(builder.Services, d => d.ServiceType == typeof(ITeamMemberRemovalParticipant)
            && d.ImplementationType?.Name == "ApiKeyOwnerRemovalParticipant");
    }

    [Fact]
    public void AddApiKeyLifecycleHandler_RegistersHandler_And_Decorates()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o => o.AddApiKeyLifecycleHandler<TestLifecycleHandler>());

        Assert.Contains(builder.Services, d =>
            d.ServiceType == typeof(IApiKeyLifecycleHandler) && d.ImplementationType == typeof(TestLifecycleHandler));
        // Decoration replaces the IApiKeyAdministrationService registration with a factory.
        var admin = Assert.Single(builder.Services, d => d.ServiceType == typeof(IApiKeyAdministrationService));
        Assert.NotNull(admin.ImplementationFactory);
    }

    private sealed class TestLifecycleHandler : IApiKeyLifecycleHandler
    {
        public Task OnApiKeyLifecycleAsync(ApiKeyLifecycleContext context) => Task.CompletedTask;
    }

    [Fact]
    public void ApiKeyAdministrationService_RegisteredOnce_AsAuditedFactory()
    {
        // Regression for #87: AddThargaApiKeyAuthentication must not clobber the audit-decorated
        // registration. The audited helper registers a single resolve-time factory (not a plain
        // ImplementationType map), so audit can never be silently dropped by call order.
        var builder = CreateBuilder();
        builder.AddThargaTeam(o => o.Audit = new AuditOptions());

        var admin = Assert.Single(builder.Services, d => d.ServiceType == typeof(IApiKeyAdministrationService));
        Assert.NotNull(admin.ImplementationFactory);
        Assert.Null(admin.ImplementationType);
    }

    [Fact]
    public void SkipsApiKeyAuth_WhenNull()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o => o.ApiKey = null);

        Assert.DoesNotContain(builder.Services, d =>
            d.ServiceType == typeof(IApiKeyRepository));
    }

    [Fact]
    public void RegistersHttpContextAccessor()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam();
        var provider = builder.Services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IHttpContextAccessor>());
    }

    [Fact]
    public void UseThargaTeam_MapsAuthEndpoints()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam();

        // Stub services that are normally provided by the Blazor/MongoDB runtime
        // so that ValidateOnBuild (enabled by default in .NET 10) does not throw.
        builder.Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(
            new TestNavigationManager());
        builder.Services.AddSingleton(
            new Moq.Mock<Microsoft.JSInterop.IJSRuntime>().Object);
        builder.Services.AddSingleton(
            new Moq.Mock<Tharga.MongoDB.IMongoDbServiceFactory>().Object);

        var app = builder.Build();

        app.UseThargaTeam();

        var endpoints = ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app)
            .DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .ToList();

        Assert.Contains(endpoints, e => e.RoutePattern.RawText == "/login");
        Assert.Contains(endpoints, e => e.RoutePattern.RawText == "/logout");
    }

    private class TestNavigationManager : Microsoft.AspNetCore.Components.NavigationManager
    {
        public TestNavigationManager() => Initialize("https://localhost/", "https://localhost/");
    }

    /// <summary>
    /// Consent policy is registered as <b>the same instance</b> the host configured, not a copy.
    /// </summary>
    /// <remarks>
    /// This is what keeps one policy from becoming two. Consent decides what a caller may do in a team
    /// they do not belong to, and more than one surface answers that: the Blazor claims builder and an
    /// MCP call naming a team. When <c>ConsentOptions</c> lived in the Blazor package the MCP side could
    /// not reach it and briefly carried its own copy of the default level — which is exactly the state
    /// where the same caller reaches the same team at two different levels depending on the door.
    /// <para>
    /// Same instance rather than equal values, deliberately: a copy would pass an equality assertion on
    /// the day it was written and drift silently afterwards.
    /// </para>
    /// </remarks>
    [Fact]
    public void ConsentOptions_AreRegisteredAsTheSameInstanceEverySurfaceReads()
    {
        var builder = CreateBuilder();
        ConsentOptions configured = null;
        builder.AddThargaTeam(o =>
        {
            o.Blazor.Consent.AccessLevel = AccessLevel.User;
            o.Blazor.Consent.Roles = ["Support"];
            configured = o.Blazor.Consent;
        });

        var provider = builder.Services.BuildServiceProvider();
        var resolved = provider.GetService<IOptions<ConsentOptions>>();

        Assert.NotNull(resolved);
        Assert.Same(configured, resolved.Value);
        Assert.Equal(AccessLevel.User, resolved.Value.AccessLevel);
    }

    /// <summary>
    /// And it lives in the core assembly, so packages below the Blazor one can read it at all. Moving it
    /// back is the change that would silently reintroduce the duplicate.
    /// </summary>
    [Fact]
    public void ConsentOptions_LiveInTheCoreAssembly()
    {
        Assert.Equal("Tharga.Team", typeof(ConsentOptions).Assembly.GetName().Name);
        Assert.Equal("Tharga.Team", typeof(ConsentOptions).Namespace);
    }
}
