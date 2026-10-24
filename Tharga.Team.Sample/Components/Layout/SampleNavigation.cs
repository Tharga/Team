namespace Tharga.Team.Sample.Components.Layout;

/// <summary>
/// Every page of the sample, grouped by what it demonstrates.
/// </summary>
/// <remarks>
/// The menu and the dashboard both read this, so a page added here appears in both and the two cannot
/// disagree about where something is.
/// </remarks>
internal static class SampleNavigation
{
    public static readonly SampleNavigationGroup[] Groups =
    [
        new("My account", "person", "You: your profile, your mail and notice preferences, and invitations waiting for you.", SampleAudience.SignedIn,
        [
            new("Profile", "account", "person", "Profile and claims, mail and notice preferences, demo mode and view as. Mounted at /account to exercise o.Blazor.ProfilePath."),
            new("Invitations", "invites", "drafts", "Invitations to a team, waiting for you to accept or decline.")
        ]),
        new("Team", "groups", "The selected team: its members, roles and scopes, what you hold in it, and its keys and audit.", SampleAudience.SignedIn,
        [
            new("Team", "team", "groups", "Create a team, invite members, change access levels, and set notice defaults."),
            new("Custom roles", "roles", "admin_panel_settings", "Roles the team defines, and the scopes each one grants."),
            new("Scopes", "scopes", "shield", "Which access level holds which scope in this team."),
            new("Your access", "access", "verified_user", "Who you are here: your access level, roles, and every scope you hold."),
            new("API keys", "api-keys", "key", "The team's API keys, shared and personal.", RequiresTeam: true),
            new("Team audit", "team-audit", "receipt_long", "What happened in the team, with filters and charts.", RequiresTeam: true)
        ]),
        new("Support", "support_agent", "Asking for help, with the assistant or a person, and answering it.", SampleAudience.SignedIn,
        [
            new("Support", "support", "support_agent", "Your cases and a new one, the team's support queue, and cases that belong to no team."),
            new("AI assistant", "assistant", "smart_toy", "What the assistant answers your team with: the platform's model, Neurolito with your own key, or off.", RequiresTeam: true)
        ]),
        new("Messages", "mail", "Mail to users and to contacts, and what became of each one.", SampleAudience.Developer,
        [
            new("Messages to users", "messages", "mail", "Write to users, and the log of what was sent."),
            new("Messages to contacts", "contacts", "contact_mail", "Write to contacts, and each address with what became of it.")
        ]),
        new("System", "settings", "Administration across every team.", SampleAudience.Developer,
        [
            new("Users", "users", "people", "Every user and their teams, and each user's mail."),
            new("System audit", "audit", "receipt_long", "Everything audited, across every team."),
            new("System API keys", "system-api-keys", "vpn_key", "Keys that act across teams rather than in one."),
            new("Icon settings", "icon-settings", "image", "How the site shows team and user icons.")
        ])
    ];

    /// <summary>The group holding the page at <paramref name="relativePath"/>, or null for the home page or an unlisted one.</summary>
    public static SampleNavigationGroup GroupOf(string relativePath)
    {
        var first = (relativePath ?? string.Empty).Split('?', '#')[0].Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first == null ? null : Groups.FirstOrDefault(g => g.Items.Any(i => string.Equals(i.Path, first, StringComparison.OrdinalIgnoreCase)));
    }
}

/// <summary>One area of the sample, as a menu section and a dashboard card.</summary>
internal sealed record SampleNavigationGroup(string Text, string Icon, string Description, SampleAudience Audience, SampleNavigationItem[] Items);

/// <summary>One page. <paramref name="RequiresTeam"/> marks a page that needs a selected team.</summary>
internal sealed record SampleNavigationItem(string Text, string Path, string Icon, string Description, bool RequiresTeam = false);

/// <summary>Who a group is for.</summary>
internal enum SampleAudience
{
    SignedIn,
    Developer
}
