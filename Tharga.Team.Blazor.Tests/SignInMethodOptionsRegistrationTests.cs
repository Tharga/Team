using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The sign-in method mappings a host configures must reach the users list on both entry points. The
/// facade copies <c>o.Blazor</c> onto a fresh options object, and an option that is not carried across
/// compiles, reads naturally and does nothing — the defect class behind Tharga/Team#177.
/// </summary>
public class SignInMethodOptionsRegistrationTests
{
    private const string ValidAzureAdConfig = """
        { "AzureAd": { "Authority": "https://test.ciamlogin.com/test", "ClientId": "c", "TenantId": "t", "CallbackPath": "/signin-oidc" } }
        """;

    private static readonly Func<DirectoryUserIdentity, SignInMethod> EId = x => x.Issuer == "https://broker.example/" ? new SignInMethod("e-ID") : null;

    [Fact]
    public void TheFacadePath_RegistersTheConfiguredMappings()
    {
        var builder = WebApplication.CreateBuilder();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ValidAzureAdConfig));
        builder.Configuration.AddJsonStream(stream);
        builder.AddThargaTeam(o =>
        {
            o.Blazor.SignInMethods.Add(EId);
            o.Blazor.SignInMethods.ShowUserPrincipalName = true;
        });

        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<SignInMethodOptions>();

        Assert.Same(EId, Assert.Single(options.Mappings));
        Assert.True(options.ShowUserPrincipalName);
    }

    [Fact]
    public void TheGranularPath_RegistersTheConfiguredMappings()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamBlazor(o => o.SignInMethods.Add(EId));

        using var provider = services.BuildServiceProvider();

        Assert.Same(EId, Assert.Single(provider.GetRequiredService<SignInMethodOptions>().Mappings));
    }

    [Fact]
    public void NothingConfigured_StillRegistersEmptyOptions()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamBlazor();

        using var provider = services.BuildServiceProvider();

        Assert.Empty(provider.GetRequiredService<SignInMethodOptions>().Mappings);
    }
}
