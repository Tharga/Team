using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team;
using Tharga.Team.Entra;

namespace Tharga.Team.Entra.Tests;

/// <summary>
/// Registration and configuration binding for <c>AddThargaEntraUserDirectory</c>, plus the token
/// provider's configuration validation.
/// </summary>
public class EntraDirectoryRegistrationTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(x => x.Key, x => x.Value)).Build();

    [Fact]
    public void Registration_BindsAzureAdSectionAndAppliesOverrides()
    {
        var services = new ServiceCollection();
        services.AddThargaEntraUserDirectory(
            Config(("AzureAd:TenantId", "tid"), ("AzureAd:ClientId", "cid"), ("AzureAd:ClientSecret", "secret")),
            o => o.ClientId = "cid-override");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<EntraDirectoryOptions>>().Value;

        Assert.Equal("tid", options.TenantId);
        Assert.Equal("cid-override", options.ClientId);
        Assert.Equal("secret", options.ClientSecret);
        Assert.Equal(new Uri("https://graph.microsoft.com/v1.0/"), options.GraphBaseAddress);
    }

    [Fact]
    public void Registration_ResolvesDirectoryServiceWithGraphBaseAddress()
    {
        var services = new ServiceCollection();
        services.AddThargaEntraUserDirectory(configure: o => o.GraphBaseAddress = new Uri("https://graph.test/v1.0/"));

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IUserDirectoryService>();

        Assert.IsType<EntraUserDirectoryService>(service);
    }

    [Fact]
    public void Registration_ResolvesIdentityDirectoryAsTheEntraDirectory()
    {
        var services = new ServiceCollection();
        services.AddThargaEntraUserDirectory(configure: o => o.GraphBaseAddress = new Uri("https://graph.test/v1.0/"));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        var service = provider.GetRequiredService<IUserIdentityDirectory>();

        Assert.IsType<EntraUserDirectoryService>(service);
    }

    [Fact]
    public void Registration_KeepsAHostsOwnIdentityDirectory()
    {
        var hostOwn = Substitute.For<IUserIdentityDirectory>();
        var services = new ServiceCollection();
        services.AddSingleton(hostOwn);
        services.AddThargaEntraUserDirectory();

        using var provider = services.BuildServiceProvider();

        Assert.Same(hostOwn, provider.GetRequiredService<IUserIdentityDirectory>());
    }

    [Fact]
    public void Registration_DirectoryReplacedByOneWithoutIdentities_NamesTheMissingInterface()
    {
        var services = new ServiceCollection();
        services.AddThargaEntraUserDirectory();
        services.AddScoped(_ => Substitute.For<IUserDirectoryService>());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var ex = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IUserIdentityDirectory>());
        Assert.Contains(nameof(IUserIdentityDirectory), ex.Message);
    }

    [Fact]
    public async Task TokenProvider_MissingConfiguration_ThrowsInformative()
    {
        var provider = new CredentialEntraTokenProvider(Options.Create(new EntraDirectoryOptions()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.GetTokenAsync());

        Assert.Contains("AzureAd", ex.Message);
    }

    [Fact]
    public async Task TokenProvider_CustomCredential_IsUsed()
    {
        var credential = Substitute.For<TokenCredential>();
        credential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<AccessToken>(new AccessToken("tok-abc", DateTimeOffset.UtcNow.AddHours(1))));
        var provider = new CredentialEntraTokenProvider(Options.Create(new EntraDirectoryOptions { Credential = credential }));

        var token = await provider.GetTokenAsync();

        Assert.Equal("tok-abc", token);
    }
}
