using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>
/// Wraps the registered <see cref="ITeamInvitationService"/> so repeated failed resolves from one source are
/// delayed and the first crossing is audited.
/// </summary>
public static class InvitationThrottleRegistration
{
    /// <summary>
    /// Decorates <see cref="ITeamInvitationService"/> with the throttle. Does nothing when no invitation
    /// service is registered.
    /// </summary>
    /// <remarks>
    /// <b>Registered from this package rather than from the Blazor one</b>, because the types are internal
    /// here and a library owns registering its own services. The caller only has to say when — after the
    /// facet it decorates exists.
    /// <para>
    /// <b>Decorating by replacing the descriptor, not by ordering.</b> The facets are registered with
    /// <c>TryAdd</c>, so a decorator added first would simply win and the inner service would never be
    /// built; and one added last without removing the original would be shadowed by it. Taking the existing
    /// descriptor out and re-registering around it is what makes this independent of when it is called.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddInvitationThrottle(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var existing = services.LastOrDefault(d => d.ServiceType == typeof(ITeamInvitationService));
        if (existing == null) return services;

        services.AddOptions();
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<InvitationThrottle>();

        services.Remove(existing);
        services.Add(ServiceDescriptor.Describe(
            typeof(ITeamInvitationService),
            sp => new ThrottledTeamInvitationService(
                Inner(sp, existing),
                sp.GetRequiredService<InvitationThrottle>(),
                sp.GetService<Microsoft.Extensions.Options.IOptions<InvitationOptions>>(),
                sp.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
                sp.GetService<Audit.CompositeAuditLogger>()),
            existing.Lifetime));

        return services;
    }

    private static ITeamInvitationService Inner(IServiceProvider sp, ServiceDescriptor existing)
    {
        if (existing.ImplementationFactory != null)
            return (ITeamInvitationService)existing.ImplementationFactory(sp);

        if (existing.ImplementationInstance != null)
            return (ITeamInvitationService)existing.ImplementationInstance;

        if (existing.ImplementationType != null)
            return (ITeamInvitationService)ActivatorUtilities.CreateInstance(sp, existing.ImplementationType);

        throw new InvalidOperationException(
            "Cannot resolve the inner ITeamInvitationService to apply the invitation throttle to.");
    }
}
