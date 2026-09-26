using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Service.Email;

/// <summary>
/// In production every mail is delivered. Outside it, a recipient in an allowed domain is mailed as normal, anyone
/// else goes to the override address, and with no override address nothing is sent.
/// </summary>
/// <remarks>
/// <b>Production is <see cref="HostEnvironmentEnvExtensions.IsProduction"/>, and no host environment counts as not
/// production.</b> Both are fail-closed choices: a host gets the safe behaviour by doing nothing, and an environment
/// that cannot be identified is not assumed to be the one where customers may be mailed.
/// </remarks>
public sealed class OutboundMailPolicy : IOutboundMailPolicy
{
    private const string UnnamedEnvironment = "Non-production";

    private readonly IHostEnvironment _environment;
    private readonly string _address;
    private readonly string[] _allowedDomains;

    public OutboundMailPolicy(IOptions<OutboundMailPolicyOptions> options, IHostEnvironment environment = null)
    {
        var value = options?.Value ?? new OutboundMailPolicyOptions();

        _environment = environment;
        _address = Configured(value.Address);
        _allowedDomains = (value.AllowedDomains ?? [])
            .Select(Configured)
            .Where(x => x != null)
            .Select(x => x.TrimStart('@'))
            .ToArray();
    }

    /// <summary>True when mail goes out unchanged to everyone.</summary>
    public bool IsProduction => _environment?.IsProduction() == true;

    /// <summary>The override address in force, or null when none is configured.</summary>
    public string Address => _address;

    /// <summary>The allowed domains in force, after unset and unexpanded values are removed.</summary>
    public IReadOnlyList<string> AllowedDomains => _allowedDomains;

    public OutboundMailDecision Decide(string recipient, string subject)
    {
        if (IsProduction || IsAllowed(recipient))
            return new OutboundMailDecision(OutboundMailAction.Deliver, recipient, subject);

        if (_address == null)
            return new OutboundMailDecision(OutboundMailAction.Withhold, recipient, subject);

        var environment = _environment?.EnvironmentName ?? UnnamedEnvironment;
        return new OutboundMailDecision(OutboundMailAction.Redirect, _address, $"[{environment} -> {recipient}] {subject}");
    }

    private bool IsAllowed(string recipient)
    {
        var at = recipient?.LastIndexOf('@') ?? -1;
        if (at < 0) return false;

        var domain = recipient[(at + 1)..];
        return _allowedDomains.Any(x => string.Equals(x, domain, StringComparison.OrdinalIgnoreCase));
    }

    private static string Configured(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();
        return trimmed.StartsWith("$(", StringComparison.Ordinal) && trimmed.EndsWith(')') ? null : trimmed;
    }
}
