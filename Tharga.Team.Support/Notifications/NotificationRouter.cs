using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Notifications;

/// <summary>
/// Turns an audit entry into the messages it should produce — which channels, worded how.
/// </summary>
/// <remarks>
/// Deliberately pure and synchronous, with no transport of its own. Deciding what to say is the part
/// with rules in it, and keeping it separate from posting means the rules can be asserted without a
/// network, a background thread or a fake HTTP handler.
/// </remarks>
public sealed partial class NotificationRouter(IOptions<NotificationOptions> options)
{
    private const string Wildcard = "*";

    /// <summary>What a host writes in <see cref="NotificationOptions.CaseUrlTemplate"/> for the case.</summary>
    private const string CaseIdPlaceholder = "{caseId}";

    private readonly NotificationOptions _options = options?.Value ?? new NotificationOptions();

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex PlaceholderPattern { get; }

    /// <summary>
    /// The messages <paramref name="entry"/> should produce. Empty when no route matches — which is how
    /// an event is kept out of Slack.
    /// </summary>
    public IReadOnlyList<NotificationMessage> Route(AuditEntry entry)
    {
        if (entry == null) return [];

        var routes = _options.Routes;
        if (routes == null || routes.Count == 0) return [];

        var key = EventKey(entry);
        List<NotificationMessage> messages = null;

        foreach (var route in routes)
        {
            if (route == null) continue;
            if (!Matches(route, key, entry.Success)) continue;

            var channel = string.IsNullOrWhiteSpace(route.Channel) ? _options.DefaultChannel : route.Channel;

            // A route with no channel and no default is configuration that is not finished yet. Silently
            // skipping it is right: the alternative is posting somewhere nobody chose.
            if (string.IsNullOrWhiteSpace(channel)) continue;

            messages ??= [];
            messages.Add(new NotificationMessage(channel, Render(route.Template, entry, key, _options.CaseUrlTemplate)));
        }

        return messages ?? (IReadOnlyList<NotificationMessage>)[];
    }

    /// <summary>The routing key for an entry — <c>feature:action</c>, the same shape as a scope.</summary>
    public static string EventKey(AuditEntry entry)
    {
        if (entry == null) return null;
        if (string.IsNullOrWhiteSpace(entry.Feature)) return entry.Action;
        return string.IsNullOrWhiteSpace(entry.Action) ? entry.Feature : $"{entry.Feature}:{entry.Action}";
    }

    private static bool Matches(NotificationRoute route, string key, bool success)
    {
        if (route.Success.HasValue && route.Success.Value != success) return false;

        var pattern = route.Event;
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        if (pattern == Wildcard) return true;
        if (string.IsNullOrWhiteSpace(key)) return false;

        if (pattern.EndsWith(":*", StringComparison.Ordinal))
        {
            var feature = pattern[..^2];
            var separator = key.IndexOf(':');
            var entryFeature = separator < 0 ? key : key[..separator];
            return string.Equals(feature, entryFeature, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(pattern, key, StringComparison.OrdinalIgnoreCase);
    }

    private static string Render(string template, AuditEntry entry, string key, string caseUrlTemplate)
    {
        if (string.IsNullOrWhiteSpace(template)) return DefaultText(entry, key);

        return PlaceholderPattern.Replace(template, match => Resolve(match.Groups[1].Value, entry, key, caseUrlTemplate) ?? string.Empty);
    }

    /// <remarks>
    /// Built rather than expressed as a template string, because the useful default is conditional — a
    /// team only when there is one, an error only when it failed — and putting conditionals into the
    /// template syntax would make configuration a language.
    /// </remarks>
    private static string DefaultText(AuditEntry entry, string key)
    {
        var text = $"{key ?? "event"} by {Actor(entry)}";
        if (!string.IsNullOrWhiteSpace(entry.TeamKey)) text += $" on team {entry.TeamKey}";
        if (!entry.Success) text += $" — failed: {entry.ErrorMessage ?? "no reason given"}";
        return text;
    }

    /// <remarks>
    /// Unknown names fall through to metadata rather than being left as literal braces, so the audit
    /// vocabulary — <c>team.name</c>, <c>member.email</c> — is usable in a template with no mapping
    /// table to maintain alongside it. The cost is that a typo renders as nothing instead of announcing
    /// itself; the message being visibly wrong in Slack is the feedback.
    /// </remarks>
    private static string Resolve(string name, AuditEntry entry, string key, string caseUrlTemplate) => name.ToLowerInvariant() switch
    {
        "event" => key,
        "case.url" => CaseUrl(entry, caseUrlTemplate),
        "feature" => entry.Feature,
        "action" => entry.Action,
        "actor" => Actor(entry),
        "team" => entry.TeamKey,
        "time" => entry.Timestamp.ToString("u"),
        "outcome" => entry.Success ? "succeeded" : "failed",
        "error" => entry.ErrorMessage,
        _ => entry.Metadata != null && entry.Metadata.TryGetValue(name, out var value) ? value : null
    };

    /// <summary>
    /// The configured link to the case this entry is about, or null when there is nothing to link to.
    /// </summary>
    /// <remarks>
    /// <b>Null on either half being missing</b> — no template, or an entry that is not about a case — and
    /// <see cref="Render"/> turns that into an empty string. So a route worded with a link stays readable on
    /// a host that has not configured one, and a team event borrowing the same wording does not emit a link
    /// to a case that does not exist.
    /// <para>
    /// The case id is read from the audit metadata the support decorator already writes, which is why this
    /// needed no new plumbing to reach the router: <see cref="Resolve"/> has always fallen through to
    /// metadata for unknown names, so <c>{support.case.id}</c> worked before <c>{case.url}</c> existed.
    /// </para>
    /// </remarks>
    private static string CaseUrl(AuditEntry entry, string caseUrlTemplate)
    {
        if (string.IsNullOrWhiteSpace(caseUrlTemplate)) return null;

        if (entry.Metadata == null || !entry.Metadata.TryGetValue(SupportAuditMetadataKeys.CaseId, out var caseId))
            return null;

        return string.IsNullOrWhiteSpace(caseId)
            ? null
            : caseUrlTemplate.Replace(CaseIdPlaceholder, Uri.EscapeDataString(caseId), StringComparison.OrdinalIgnoreCase);
    }

    private static string Actor(AuditEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.CallerIdentity) ? entry.CallerIdentity : "an unknown caller";
}

/// <summary>A message the router decided to send, and where it goes.</summary>
/// <param name="Channel">The resolved channel — the route's own, or the configured default.</param>
/// <param name="Text">The rendered message.</param>
public readonly record struct NotificationMessage(string Channel, string Text);
