using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Email.Invitations;

/// <summary>
/// Sends team invitations by email over SMTP.
/// </summary>
public static class InvitationMailRegistration
{
    /// <summary>
    /// Registers the SMTP invitation sender. A sender the host registered itself wins; settings come from
    /// <see cref="EmailOptions"/> — <c>ThargaBlazorOptions.Email</c> sets them, or <paramref name="configure"/> does.
    /// </summary>
    public static IServiceCollection AddThargaEmailInvitations(this IServiceCollection services, Action<EmailOptions> configure = null)
    {
        if (configure != null) services.Configure(configure);

        services.AddOutboundMailPolicy();
        services.TryAddScoped<ITeamEmailSender, InvitationMailSender>();

        return services;
    }
}
