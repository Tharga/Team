using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Notifications;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The routing rules — which events reach Slack, where they go, and how they read.
/// </summary>
/// <remarks>
/// The router is pure, so every rule the README states can be asserted here without a network, a
/// background pump or a fake HTTP handler. What the transport does with the result is
/// <see cref="SlackNotificationSinkTests"/>.
/// </remarks>
public class NotificationRouterTests
{
    private static AuditEntry Entry(
        string feature,
        string action,
        string teamKey = "team-1",
        string caller = "alice",
        bool success = true,
        string error = null,
        Dictionary<string, string> metadata = null)
        => new()
        {
            Timestamp = new DateTime(2026, 8, 2, 9, 30, 0, DateTimeKind.Utc),
            EventType = AuditEventType.ServiceCall,
            Feature = feature,
            Action = action,
            TeamKey = teamKey,
            CallerIdentity = caller,
            Success = success,
            ErrorMessage = error,
            Metadata = metadata
        };

    private static NotificationRouter Router(params NotificationRoute[] routes)
        => Router("#default", routes);

    private static NotificationRouter Router(string defaultChannel, params NotificationRoute[] routes)
        => new(Options.Create(new NotificationOptions { DefaultChannel = defaultChannel, Routes = routes }));

    // --- the allowlist ---

    [Fact]
    public void ARoutedEvent_Posts()
    {
        var router = Router(new NotificationRoute { Event = "team:create", Channel = "#teams" });

        var messages = router.Route(Entry("team", "create"));

        var message = Assert.Single(messages);
        Assert.Equal("#teams", message.Channel);
    }

    /// <summary>
    /// The table <i>is</i> the allowlist — there is no second concept that could disagree with it.
    /// </summary>
    [Fact]
    public void AnUnroutedEvent_IsNotSent()
    {
        var router = Router(new NotificationRoute { Event = "team:create", Channel = "#teams" });

        Assert.Empty(router.Route(Entry("team", "delete")));
    }

    /// <summary>
    /// The constraint the spec marks non-optional: removing an event is configuration, never a code
    /// change. Same router type, same entry, different options — and the posts stop.
    /// </summary>
    [Fact]
    public void RemovingARoute_StopsThePosts_WithNoCodeChange()
    {
        var entry = Entry("team", "create");
        var route = new NotificationRoute { Event = "team:create", Channel = "#teams" };

        Assert.Single(Router(route).Route(entry));
        Assert.Empty(Router().Route(entry));
    }

    [Fact]
    public void AnEmptyTable_SendsNothing()
    {
        Assert.Empty(Router().Route(Entry("team", "create")));
    }

    // --- routing, not a flat allowlist ---

    /// <summary>Two events, two channels — the thing an allowlist cannot express.</summary>
    [Fact]
    public void TwoEvents_CanGoToDifferentChannels()
    {
        var router = Router(
            new NotificationRoute { Event = "team:create", Channel = "#teams" },
            new NotificationRoute { Event = "user:delete", Channel = "#security" });

        Assert.Equal("#teams", Assert.Single(router.Route(Entry("team", "create"))).Channel);
        Assert.Equal("#security", Assert.Single(router.Route(Entry("user", "delete"))).Channel);
    }

    /// <summary>And one event can go to two, which is why every match fires rather than the first.</summary>
    [Fact]
    public void OneEvent_CanGoToTwoChannels()
    {
        var router = Router(
            new NotificationRoute { Event = "team:create", Channel = "#teams" },
            new NotificationRoute { Event = "team:create", Channel = "#audit" });

        Assert.Equal(["#teams", "#audit"], router.Route(Entry("team", "create")).Select(x => x.Channel));
    }

    [Fact]
    public void AFeatureWildcard_MatchesEveryActionOnThatFeature()
    {
        var router = Router(new NotificationRoute { Event = "team:*", Channel = "#teams" });

        Assert.Single(router.Route(Entry("team", "create")));
        Assert.Single(router.Route(Entry("team", "remove-member")));
        Assert.Empty(router.Route(Entry("user", "delete")));
    }

    [Fact]
    public void TheFullWildcard_MatchesEverything()
    {
        var router = Router(new NotificationRoute { Event = "*", Channel = "#everything" });

        Assert.Single(router.Route(Entry("team", "create")));
        Assert.Single(router.Route(Entry("invoice", "paid")));
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        var router = Router(new NotificationRoute { Event = "Team:Create", Channel = "#teams" });

        Assert.Single(router.Route(Entry("team", "create")));
    }

    /// <summary>A failures channel is configuration, so a success-worded route never narrates a throw.</summary>
    [Fact]
    public void ARouteCanBeRestrictedToFailures()
    {
        var router = Router(new NotificationRoute { Event = "team:*", Channel = "#alerts", Success = false });

        Assert.Empty(router.Route(Entry("team", "create")));
        Assert.Single(router.Route(Entry("team", "create", success: false, error: "boom")));
    }

    [Fact]
    public void ARouteCanBeRestrictedToSuccesses()
    {
        var router = Router(new NotificationRoute { Event = "team:*", Channel = "#teams", Success = true });

        Assert.Single(router.Route(Entry("team", "create")));
        Assert.Empty(router.Route(Entry("team", "create", success: false, error: "boom")));
    }

    // --- channels ---

    [Fact]
    public void ARouteWithNoChannel_UsesTheDefault()
    {
        var router = Router("#fallback", new NotificationRoute { Event = "team:create" });

        Assert.Equal("#fallback", Assert.Single(router.Route(Entry("team", "create"))).Channel);
    }

    /// <summary>
    /// No channel and no default is unfinished configuration. Skipping is right — the alternative is
    /// posting into a channel nobody chose.
    /// </summary>
    [Fact]
    public void ARouteWithNoChannelAndNoDefault_SendsNothing()
    {
        var router = Router(defaultChannel: null, new NotificationRoute { Event = "team:create" });

        Assert.Empty(router.Route(Entry("team", "create")));
    }

    // --- message content ---

    /// <summary>
    /// The point of routing over an allowlist: a channel says what happened, not "a ServiceCall
    /// occurred". This is the assertion that would have failed had the audit entries carried no
    /// operation metadata.
    /// </summary>
    [Fact]
    public void TheMessageSaysWhatHappened_NotJustThatSomethingDid()
    {
        var router = Router(new NotificationRoute
        {
            Event = "team:create",
            Channel = "#teams",
            Template = "New team *{team.name}* created by {actor}."
        });

        var entry = Entry("team", "create", metadata: new Dictionary<string, string> { [AuditMetadataKeys.TeamName] = "Acme" });

        Assert.Equal("New team *Acme* created by alice.", Assert.Single(router.Route(entry)).Text);
    }

    [Fact]
    public void TwoEvents_CanBeWordedDifferently()
    {
        var router = Router(
            new NotificationRoute { Event = "team:create", Channel = "#teams", Template = "Team {team.name} created." },
            new NotificationRoute { Event = "team:invite", Channel = "#teams", Template = "{member.email} was invited." });

        var created = Entry("team", "create", metadata: new Dictionary<string, string> { [AuditMetadataKeys.TeamName] = "Acme" });
        var invited = Entry("team", "invite", metadata: new Dictionary<string, string> { [AuditMetadataKeys.MemberEmail] = "bob@example.com" });

        Assert.Equal("Team Acme created.", Assert.Single(router.Route(created)).Text);
        Assert.Equal("bob@example.com was invited.", Assert.Single(router.Route(invited)).Text);
    }

    [Theory]
    [InlineData("{event}", "team:create")]
    [InlineData("{feature}", "team")]
    [InlineData("{action}", "create")]
    [InlineData("{actor}", "alice")]
    [InlineData("{team}", "team-1")]
    [InlineData("{outcome}", "succeeded")]
    [InlineData("{time}", "2026-08-02 09:30:00Z")]
    public void EveryDocumentedPlaceholderResolves(string template, string expected)
    {
        var router = Router(new NotificationRoute { Event = "team:create", Channel = "#teams", Template = template });

        Assert.Equal(expected, Assert.Single(router.Route(Entry("team", "create"))).Text);
    }

    /// <summary>Metadata keys work directly, so there is no mapping table to maintain beside the audit vocabulary.</summary>
    [Fact]
    public void AnUnknownPlaceholder_ResolvesFromMetadata()
    {
        var router = Router(new NotificationRoute { Event = "invoice:paid", Channel = "#billing", Template = "Invoice {invoice.number}." });
        var entry = Entry("invoice", "paid", metadata: new Dictionary<string, string> { ["invoice.number"] = "INV-9" });

        Assert.Equal("Invoice INV-9.", Assert.Single(router.Route(entry)).Text);
    }

    /// <summary>A typo renders as empty rather than leaking braces into the channel.</summary>
    [Fact]
    public void APlaceholderThatResolvesToNothing_RendersEmpty()
    {
        var router = Router(new NotificationRoute { Event = "team:create", Channel = "#teams", Template = "[{no.such.key}]" });

        Assert.Equal("[]", Assert.Single(router.Route(Entry("team", "create"))).Text);
    }

    [Fact]
    public void WithNoTemplate_TheDefaultTextNamesTheEvent_TheActorAndTheTeam()
    {
        var router = Router(new NotificationRoute { Event = "team:create", Channel = "#teams" });

        Assert.Equal("team:create by alice on team team-1", Assert.Single(router.Route(Entry("team", "create"))).Text);
    }

    [Fact]
    public void WithNoTemplate_AFailureSaysWhy()
    {
        var router = Router(new NotificationRoute { Event = "team:create", Channel = "#teams" });

        var text = Assert.Single(router.Route(Entry("team", "create", success: false, error: "duplicate name"))).Text;

        Assert.Contains("failed: duplicate name", text);
    }

    [Fact]
    public void AnUnknownCaller_IsNamedRatherThanLeftBlank()
    {
        var router = Router(new NotificationRoute { Event = "team:create", Channel = "#teams", Template = "{actor}" });

        Assert.Equal("an unknown caller", Assert.Single(router.Route(Entry("team", "create", caller: null))).Text);
    }

    // --- a consumer's own events ---

    /// <summary>
    /// The same path, with no registration step and no second mechanism — an entry a host built through
    /// <c>IAuditEntryFactory</c> routes on its own feature and action like any other.
    /// </summary>
    [Fact]
    public void AConsumersOwnEvent_RoutesLikeAnyOther()
    {
        var router = Router(new NotificationRoute { Event = "invoice:paid", Channel = "#billing" });

        Assert.Equal("#billing", Assert.Single(router.Route(Entry("invoice", "paid"))).Channel);
    }

    // --- shapes that must not throw on the audited operation's thread ---

    [Fact]
    public void ANullEntry_IsIgnored()
    {
        Assert.Empty(Router(new NotificationRoute { Event = "*", Channel = "#x" }).Route(null));
    }

    [Fact]
    public void AnEntryWithNoAction_RoutesOnItsFeatureAlone()
    {
        var router = Router(new NotificationRoute { Event = "auth", Channel = "#auth" });

        Assert.Single(router.Route(Entry("auth", null)));
    }

    // --- the built-in routes ---

    /// <summary>
    /// The built-ins name no channel, so a host that registered the package but has not chosen one
    /// stays silent rather than posting somewhere arbitrary.
    /// </summary>
    [Fact]
    public void TheBuiltInRoutes_AreDormantUntilAChannelIsNamed()
    {
        var options = new NotificationOptions();
        Assert.All(options.Routes, r => Assert.Null(r.Channel));

        var silent = new NotificationRouter(Options.Create(new NotificationOptions()));
        Assert.Empty(silent.Route(Entry("team", "create")));
    }

    [Fact]
    public void TheBuiltInRoutes_WorkOnceAChannelIsNamed()
    {
        var router = new NotificationRouter(Options.Create(new NotificationOptions { DefaultChannel = "#team-events" }));

        var entry = Entry("team", "create", metadata: new Dictionary<string, string> { [AuditMetadataKeys.TeamName] = "Acme" });
        var message = Assert.Single(router.Route(entry));

        Assert.Equal("#team-events", message.Channel);
        Assert.Contains("Acme", message.Text);
    }

    /// <summary>
    /// Every built-in route names an event the toolkit actually emits. A default naming something
    /// nothing raises looks configured and does nothing — which is exactly why "user logs on" and
    /// "user created" are absent from the list despite the issue naming them.
    /// </summary>
    [Fact]
    public void EveryBuiltInRoute_NamesAnEventTheToolkitEmits()
    {
        // Sourced from the auditing decorators' Feature constants and Log(action) call sites.
        string[] emitted =
        [
            "team:create", "team:rename", "team:delete", "team:invite", "team:remove-member",
            "team:remove-member-all", "team:set-role", "team:set-member-name", "team:set-consent",
            "team:set-tenant-roles", "team:set-scope-overrides", "team:set-custom-roles",
            "team:assign-owner", "team:transfer-ownership", "team:icon-set", "team:icon-clear",
            "user:verify", "user:verify-all", "user:delete", "user:set-user-name",

            // AuditingSupportCaseServiceDecorator, Feature "support". Absent from this list until a default
            // route named one — which is the point of checking the list against the decorators rather than
            // appending whatever a new default happens to say.
            "support:raise", "support:reply", "support:close", "support:reopen"
        ];

        Assert.All(NotificationOptions.DefaultRoutes(), route => Assert.Contains(route.Event, emitted));
    }
}
