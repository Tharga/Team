using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Service.Email;

public static class OutboundMailPolicyRegistration
{
    /// <summary>
    /// Registers <see cref="IOutboundMailPolicy"/>, binds <see cref="OutboundMailPolicyOptions"/> from the
    /// <c>Email:Override</c> section of the container's <see cref="IConfiguration"/>, and reports the policy in
    /// force at startup outside production.
    /// </summary>
    /// <remarks>
    /// Safe to call more than once: every module that sends mail calls it, and the second call adds nothing.
    /// A host with its own sender calls it too, then injects <see cref="IOutboundMailPolicy"/> to apply the same
    /// rules. Configure in code with <c>services.Configure&lt;OutboundMailPolicyOptions&gt;(…)</c>.
    /// </remarks>
    public static IServiceCollection AddOutboundMailPolicy(this IServiceCollection services)
    {
        services.AddOptions();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<OutboundMailPolicyOptions>, OutboundMailPolicyConfiguration>());
        services.TryAddSingleton<IOutboundMailPolicy, OutboundMailPolicy>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboundMailPolicyReport>());

        return services;
    }
}

/// <summary>Binds the <c>Email:Override</c> section when the container has a configuration.</summary>
internal sealed class OutboundMailPolicyConfiguration(IConfiguration configuration = null) : IConfigureOptions<OutboundMailPolicyOptions>
{
    public void Configure(OutboundMailPolicyOptions options)
    {
        configuration?.GetSection(OutboundMailPolicyOptions.SectionName).Bind(options);
    }
}

/// <summary>
/// States at startup, outside production, where mail will go — so a test environment that has gone quiet says
/// why, rather than looking like a broken mail server.
/// </summary>
internal sealed class OutboundMailPolicyReport(
    IOutboundMailPolicy policy,
    IHostEnvironment environment = null,
    ILogger<OutboundMailPolicyReport> logger = null) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (policy is not OutboundMailPolicy known || known.IsProduction) return Task.CompletedTask;

        var name = environment?.EnvironmentName ?? "an unidentified environment";
        var domains = known.AllowedDomains.Count == 0 ? "none" : string.Join(", ", known.AllowedDomains);

        if (known.Address == null)
        {
            logger?.LogWarning(
                "Running in {Environment}: mail is sent only to allowed domains ({Domains}); everything else is withheld. " +
                "Set {Section}:Address to receive it instead.",
                name, domains, OutboundMailPolicyOptions.SectionName);
        }
        else
        {
            logger?.LogInformation(
                "Running in {Environment}: mail to allowed domains ({Domains}) is delivered; everything else goes to {Address}.",
                name, domains, known.Address);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
