using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>
/// Fails startup when <see cref="ApiKeyOptions.RequirePrivateKeys"/> is on in simple mode
/// (<see cref="ApiKeyOptions.AdvancedMode"/> off).
/// </summary>
/// <remarks>
/// The two cannot both hold. Simple mode auto-creates team keys that have no owner and shows no create card, so
/// with the option on a team would end up with no keys and no way to make one — a page that is quietly empty
/// rather than an error naming the setting. Checked at startup rather than at registration because the options
/// arrive through <c>Configure</c>, and only the bound value is the one in force.
/// </remarks>
internal sealed class RequirePrivateKeysCheck(IOptions<ApiKeyOptions> options) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var value = options.Value;
        if (value.RequirePrivateKeys && !value.AdvancedMode)
            throw new InvalidOperationException(PrivateKeyRequirement.SimpleModeMessage);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
