using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;
using Tharga.Toolkit.Password;

namespace Tharga.Team.Service.Tests;

public class ApiKeyAuditWiringTests
{
    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IApiKeyRepository>());
        services.AddSingleton(Substitute.For<IApiKeyService>());
        services.AddSingleton(Substitute.For<IHttpContextAccessor>());
        return services;
    }

    private static void AddAudit(ServiceCollection services)
    {
        services.AddSingleton(Options.Create(new AuditOptions()));
        services.AddSingleton<CompositeAuditLogger>();
    }

    [Fact]
    public void AuditedRegistration_WrapsWithAuditDecorator_WhenAuditConfigured()
    {
        var services = BaseServices();
        AddAudit(services);
        services.AddAuditedApiKeyAdministrationService(typeof(ApiKeyAdministrationService));

        var resolved = services.BuildServiceProvider().GetRequiredService<IApiKeyAdministrationService>();

        Assert.IsType<AuditingApiKeyServiceDecorator>(resolved);
    }

    [Fact]
    public void AuditedRegistration_ReturnsPlainService_WhenAuditNotConfigured()
    {
        var services = BaseServices();
        services.AddAuditedApiKeyAdministrationService(typeof(ApiKeyAdministrationService));

        var resolved = services.BuildServiceProvider().GetRequiredService<IApiKeyAdministrationService>();

        Assert.IsType<ApiKeyAdministrationService>(resolved);
    }

    [Fact]
    public void AuditedRegistration_StaysAudited_WhenRegisteredAgainAfterwards()
    {
        // Regression for #87: a second entry point registering the service must not discard the audit
        // decorator, and there must be no leftover duplicate registration.
        var services = BaseServices();
        AddAudit(services);
        services.AddAuditedApiKeyAdministrationService(typeof(ApiKeyAdministrationService));
        services.AddAuditedApiKeyAdministrationService(typeof(ApiKeyAdministrationService));

        Assert.Single(services, d => d.ServiceType == typeof(IApiKeyAdministrationService));
        var resolved = services.BuildServiceProvider().GetRequiredService<IApiKeyAdministrationService>();
        Assert.IsType<AuditingApiKeyServiceDecorator>(resolved);
    }
}
