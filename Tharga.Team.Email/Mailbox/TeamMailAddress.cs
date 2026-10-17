using System.Text.RegularExpressions;

namespace Tharga.Team.Email.Mailbox;

/// <summary>
/// Builds a team's address, <c>{key}{suffix}[+{itemKey}]@{domain}</c>, and reads one back.
/// </summary>
internal static partial class TeamMailAddress
{
    public static string For(string key, TeamMailboxOptions options) => $"{key}{options.Suffix}@{DomainOf(options)}";

    public static string ForItem(string key, string itemKey, TeamMailboxOptions options)
    {
        if (string.IsNullOrEmpty(itemKey) || itemKey.Any(c => c is '+' or '@' || char.IsWhiteSpace(c))) throw new ArgumentException($"'{itemKey}' cannot be part of an address.", nameof(itemKey));

        return $"{key}{options.Suffix}+{itemKey}@{DomainOf(options)}";
    }

    /// <summary>The key and item of an address on the team domain; null for any other address.</summary>
    public static ParsedTeamMailAddress Parse(string address, TeamMailboxOptions options)
    {
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(options.Domain)) return null;

        var parts = address.Trim().Trim('<', '>').ToLowerInvariant().Split('@');
        if (parts.Length != 2 || parts[1] != options.Domain.ToLowerInvariant()) return null;

        var plus = parts[0].IndexOf('+');
        var local = plus < 0 ? parts[0] : parts[0][..plus];
        var itemKey = plus < 0 ? null : parts[0][(plus + 1)..];

        var suffix = (options.Suffix ?? string.Empty).ToLowerInvariant();
        if (!local.EndsWith(suffix, StringComparison.Ordinal)) return null;

        var key = local[..^suffix.Length];
        if (!KeyPattern().IsMatch(key) || itemKey == string.Empty) return null;

        return new ParsedTeamMailAddress(key, itemKey);
    }

    private static string DomainOf(TeamMailboxOptions options) =>
        string.IsNullOrWhiteSpace(options.Domain) ? throw new InvalidOperationException("No domain is configured for team mail addresses.") : options.Domain.ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9]+$")]
    private static partial Regex KeyPattern();
}
