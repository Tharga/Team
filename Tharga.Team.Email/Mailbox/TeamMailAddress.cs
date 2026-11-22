using System.Text.RegularExpressions;

namespace Tharga.Team.Email.Mailbox;

/// <summary>
/// Builds a team's address, <c>{prefix}{key}{suffix}[+{itemKey}]@{domain}</c>, and reads one back, in the configured format or an accepted earlier one.
/// </summary>
internal static partial class TeamMailAddress
{
    public static string For(string key, TeamMailboxOptions options) => $"{options.Prefix}{key}{options.Suffix}@{DomainOf(options)}";

    public static string ForItem(string key, string itemKey, TeamMailboxOptions options)
    {
        if (string.IsNullOrEmpty(itemKey) || itemKey.Any(c => c is '+' or '@' || char.IsWhiteSpace(c))) throw new ArgumentException($"'{itemKey}' cannot be part of an address.", nameof(itemKey));

        return $"{options.Prefix}{key}{options.Suffix}+{itemKey}@{DomainOf(options)}";
    }

    /// <summary>The key and item of an address on the team domain, in the configured format first and then each accepted one; null for any other address.</summary>
    public static ParsedTeamMailAddress Parse(string address, TeamMailboxOptions options)
    {
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(options.Domain)) return null;

        var parts = address.Trim().Trim('<', '>').ToLowerInvariant().Split('@');
        if (parts.Length != 2 || parts[1] != options.Domain.ToLowerInvariant()) return null;

        var plus = parts[0].IndexOf('+');
        var local = plus < 0 ? parts[0] : parts[0][..plus];
        var itemKey = plus < 0 ? null : parts[0][(plus + 1)..];
        if (itemKey == string.Empty) return null;

        // The key is letters and digits only, so no prefix or suffix with a hyphen in it can be mistaken for part of one.
        return ReadKey(local, itemKey, options.Prefix, options.Suffix)
               ?? (options.AcceptedFormats ?? []).Select(x => x == null ? null : ReadKey(local, itemKey, x.Prefix, x.Suffix)).FirstOrDefault(x => x != null);
    }

    private static ParsedTeamMailAddress ReadKey(string local, string itemKey, string prefix, string suffix)
    {
        prefix = (prefix ?? string.Empty).ToLowerInvariant();
        suffix = (suffix ?? string.Empty).ToLowerInvariant();

        if (local.Length < prefix.Length + suffix.Length) return null;
        if (!local.StartsWith(prefix, StringComparison.Ordinal) || !local.EndsWith(suffix, StringComparison.Ordinal)) return null;

        var key = local[prefix.Length..^suffix.Length];
        return KeyPattern().IsMatch(key) ? new ParsedTeamMailAddress(key, itemKey) : null;
    }

    private static string DomainOf(TeamMailboxOptions options) =>
        string.IsNullOrWhiteSpace(options.Domain) ? throw new InvalidOperationException("No domain is configured for team mail addresses.") : options.Domain.ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9]+$")]
    private static partial Regex KeyPattern();
}
