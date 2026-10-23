using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Notifications;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The <c>{team.display}</c> placeholder and the default routes that use it (Tharga/Team#348): a channel reads a
/// team's name, not its key, and never an empty gap where the team should be.
/// </summary>
public class TeamDisplayPlaceholderTests
{
    private const string TeamKey = "1NI5A35BYXSM";

    private static AuditEntry Entry(string feature, string action, Dictionary<string, string> metadata)
        => new()
        {
            Timestamp = new DateTime(2026, 10, 22, 9, 30, 0, DateTimeKind.Utc),
            EventType = AuditEventType.ServiceCall,
            Feature = feature,
            Action = action,
            TeamKey = TeamKey,
            CallerIdentity = "alice",
            Success = true,
            Metadata = metadata
        };

    private static string Render(string template, AuditEntry entry)
    {
        var route = new NotificationRoute { Event = $"{entry.Feature}:{entry.Action}", Template = template };
        var router = new NotificationRouter(Options.Create(new NotificationOptions { DefaultChannel = "#all", Routes = [route] }));
        return Assert.Single(router.Route(entry)).Text;
    }

    private static string RenderDefault(AuditEntry entry)
    {
        var router = new NotificationRouter(Options.Create(new NotificationOptions { DefaultChannel = "#all" }));
        return Assert.Single(router.Route(entry)).Text;
    }

    [Fact]
    public void TeamDisplay_IsTheName_WhenTheEntryCarriesOne()
    {
        var entry = Entry("team", "invite", new() { [AuditMetadataKeys.TeamName] = "Acme" });

        Assert.Equal("Acme", Render("{team.display}", entry));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TeamDisplay_FallsBackToTheKey_WhenThereIsNoName(string name)
    {
        var metadata = name == null ? null : new Dictionary<string, string> { [AuditMetadataKeys.TeamName] = name };

        Assert.Equal(TeamKey, Render("{team.display}", Entry("team", "invite", metadata)));
    }

    [Fact]
    public void TheTeamPlaceholder_StillMeansTheKey()
    {
        var entry = Entry("team", "invite", new() { [AuditMetadataKeys.TeamName] = "Acme" });

        Assert.Equal(TeamKey, Render("{team}", entry));
    }

    [Fact]
    public void TheInviteRoute_NamesTheTeam()
    {
        var entry = Entry("team", "invite", new() { [AuditMetadataKeys.TeamName] = "Acme", ["member.email"] = "a@a.a" });

        Assert.Equal("alice invited *a@a.a* to *Acme*.", RenderDefault(entry));
    }

    [Fact]
    public void TheRemoveMemberRoute_NamesTheTeam()
    {
        var entry = Entry("team", "remove-member", new() { [AuditMetadataKeys.TeamName] = "Acme" });

        Assert.Equal("alice removed a member from *Acme*.", RenderDefault(entry));
    }

    [Fact]
    public void TheSupportRaiseRoute_NamesTheTeam()
    {
        var entry = Entry("support", "raise", new() { [AuditMetadataKeys.TeamName] = "Acme", ["support.case.subject"] = "Help" });

        Assert.Contains("on *Acme*.", RenderDefault(entry));
    }

    [Fact]
    public void AnInviteForATeamWithNoReadableName_StillReadsWithTheKey()
    {
        var entry = Entry("team", "invite", new() { ["member.email"] = "a@a.a" });

        Assert.Equal($"alice invited *a@a.a* to *{TeamKey}*.", RenderDefault(entry));
    }

    [Fact]
    public void TheDefaultTextForAnUnroutedWording_AlsoPrefersTheName()
    {
        var entry = Entry("team", "rename", new() { [AuditMetadataKeys.TeamName] = "Acme" });
        var router = new NotificationRouter(Options.Create(new NotificationOptions
        {
            DefaultChannel = "#all",
            Routes = [new NotificationRoute { Event = "team:rename" }]
        }));

        Assert.Contains("on team Acme", Assert.Single(router.Route(entry)).Text);
    }
}
