using Tharga.Team.Blazor.Features.Audit;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The audit log can be filtered on what an operation was done to, not only on who did it (Tharga/Team#404): a filter in the bar, a pin for the dialogs on a
/// user, a team or a member, and the target shown in an entry's detail.
/// </summary>
public class AuditTargetFilterTests
{
    [Fact]
    public void TheKindAndKeyTyped_AreCarriedIntoTheQuery()
    {
        var view = new AuditLogView { _filterTargetKind = AuditTargetKinds.User, _filterTargetKey = "u-42" };

        var query = view.BuildQuery();

        Assert.Equal(AuditTargetKinds.User, query.TargetKind);
        Assert.Equal("u-42", query.TargetKey);
    }

    /// <summary>A key pasted with its surrounding spaces still finds the entries.</summary>
    [Fact]
    public void TheKeyTyped_IsTrimmed()
        => Assert.Equal("u-42", new AuditLogView { _filterTargetKey = "  u-42 " }.BuildQuery().TargetKey);

    /// <summary>An empty filter asks for nothing, so entries with no target, and those written before targets were recorded, stay in.</summary>
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "   ")]
    [InlineData(null, null)]
    public void NothingTyped_AsksForNoTarget(string kind, string key)
    {
        var query = new AuditLogView { _filterTargetKind = kind, _filterTargetKey = key }.BuildQuery();

        Assert.Null(query.TargetKind);
        Assert.Null(query.TargetKey);
    }

    /// <summary>A dialog opened on one user shows what was done to that user, and the reader cannot edit their way out of it.</summary>
    [Fact]
    public void APinnedTarget_WinsOverWhatIsTyped()
    {
        var view = new AuditLogView
        {
            PinnedFilter = new AuditPinnedFilter { TargetKind = AuditTargetKinds.User, TargetKey = "u-1" },
            _filterTargetKind = AuditTargetKinds.Team,
            _filterTargetKey = "somebody-else"
        };

        var query = view.BuildQuery();

        Assert.Equal(AuditTargetKinds.User, query.TargetKind);
        Assert.Equal("u-1", query.TargetKey);
    }

    /// <summary>A member's dialog is confined to the team as well as to the person, so a team administrator stays inside their own tenant.</summary>
    [Fact]
    public void AMemberPin_KeepsTheTeamAsWellAsTheTarget()
    {
        var view = new AuditLogView { PinnedFilter = new AuditPinnedFilter { TargetKind = AuditTargetKinds.User, TargetKey = "u-1", TeamKey = "acme" } };

        var query = view.BuildQuery();

        Assert.Equal("acme", query.TeamKey);
        Assert.Equal("u-1", query.TargetKey);
    }

    [Fact]
    public void TheKindsOfThingAnEntryWasDoneTo_AreNamedInWords_AndAHostsOwnIsShownAsItIs()
    {
        var view = new AuditLogView();

        Assert.Equal("User", view.TargetKindLabel(AuditTargetKinds.User));
        Assert.Equal("Team", view.TargetKindLabel(AuditTargetKinds.Team));
        Assert.Equal("API key", view.TargetKindLabel(AuditTargetKinds.ApiKey));
        Assert.Equal("invoice", view.TargetKindLabel("invoice"));
    }

    [Fact]
    public void EveryTextTheFilterAndTheDetailRender_IsLocalizable()
    {
        Assert.Contains(AuditLogViewText.DoneTo, AuditLogViewText.All);
        Assert.Contains(AuditLogViewText.DoneToKey, AuditLogViewText.All);
        Assert.Contains(AuditLogViewText.DetailDoneTo, AuditLogViewText.All);
        Assert.Contains(AuditLogViewText.TargetUser, AuditLogViewText.All);
        Assert.Contains(AuditLogViewText.TargetTeam, AuditLogViewText.All);
        Assert.Contains(AuditLogViewText.TargetApiKey, AuditLogViewText.All);
    }
}
