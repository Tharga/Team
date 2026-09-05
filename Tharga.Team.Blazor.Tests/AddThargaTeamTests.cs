using Microsoft.AspNetCore.Authentication;
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

    [Fact]
    public void RegistersControllerServices()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam();

        Assert.Contains(builder.Services, d => d.ServiceType == typeof(ThargaControllerOptions));
    }

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
