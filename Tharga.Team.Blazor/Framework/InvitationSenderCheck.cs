using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Warns at startup when invitation email is configured but nothing sends it.
/// </summary>
/// <remarks>
/// <b>The fallback looks exactly like success.</b> With no sender, the invite dialogs offer the link to copy
/// instead, which is a supported configuration — so a host that set SMTP settings and forgot the sender sees no
/// error, only invitations that never arrive. The SMTP sender lives in <c>Tharga.Team.Email</c>, so this package
/// cannot register it, only say that it is missing.
/// </remarks>
internal sealed class InvitationSenderCheck(IServiceProvider serviceProvider, ILogger<InvitationSenderCheck> logger = null) : IHostedService
{
    internal const string Message =
        "Invitation email is configured (Email.SmtpHost), but no ITeamEmailSender is registered, so invitations are offered as a link to copy and never sent. " +
        "Install Tharga.Team.Email and call services.AddThargaEmailInvitations(), or register your own sender with AddEmailService<T>().";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        if (scope.ServiceProvider.GetService<ITeamEmailSender>() == null) logger?.LogWarning(Message);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
