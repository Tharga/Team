namespace Tharga.Team.Service.Email;

/// <summary>
/// Where mail may go outside production. Bound from the <c>Email:Override</c> configuration section.
/// </summary>
/// <remarks>
/// Ignored in production. Outside it, a recipient in one of <see cref="AllowedDomains"/> is mailed as normal,
/// anyone else is redirected to <see cref="Address"/>, and with no <see cref="Address"/> they are not mailed at
/// all. A value still shaped like an unexpanded pipeline variable, <c>$(Name)</c>, counts as unset.
/// </remarks>
public class OutboundMailPolicyOptions
{
    /// <summary>The configuration section the options are bound from.</summary>
    public const string SectionName = "Email:Override";

    /// <summary>Where non-production mail to anyone outside <see cref="AllowedDomains"/> is sent instead.</summary>
    public string Address { get; set; }

    /// <summary>
    /// Domains mailed as normal even outside production, such as the team's own. Matched exactly and
    /// case-insensitively on the part after <c>@</c>, so a subdomain has to be listed on its own.
    /// </summary>
    public string[] AllowedDomains { get; set; } = [];
}
