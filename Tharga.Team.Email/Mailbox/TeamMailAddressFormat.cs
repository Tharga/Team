namespace Tharga.Team.Email.Mailbox;

/// <summary>
/// A way a team's address was once made, <c>{Prefix}{key}{Suffix}[+{itemKey}]@{domain}</c>, listed in <see cref="TeamMailboxOptions.AcceptedFormats"/> so its
/// addresses still route.
/// </summary>
public class TeamMailAddressFormat
{
    /// <summary>Written before the key; empty for none.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Written after the key; empty for none.</summary>
    public string Suffix { get; set; } = string.Empty;
}
