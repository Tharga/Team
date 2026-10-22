using Tharga.Team;
using Tharga.Team.Blazor.Features.Scopes;

namespace Tharga.Team.Blazor.Tests;

public class ScopeGroupingTests
{
    private static readonly ScopeGroup[] Groups = [new("platform", "Platform"), new("diary", "Diary")];

    private static ScopeRow Row(string name, string group = null) => new(name, null, [], [], false, group);

    private static string Heading(string key) => key.ToUpperInvariant();

    [Fact]
    public void NothingGrouped_IsOneSectionWithoutHeading_InTheIncomingOrder()
    {
        var rows = new[] { Row("b"), Row("a") };

        var sections = ScopeGrouping.Sections(rows, r => r.Group, [], Heading, "Other");

        var section = Assert.Single(sections);
        Assert.Null(section.Heading);
        Assert.Equal(["b", "a"], section.Items.Select(r => r.Name));
    }

    [Fact]
    public void Groups_AreInTheHostOrder_NotAlphabetical()
    {
        var rows = new[] { Row("case:read", "diary"), Row("team:read", "platform") };

        var sections = ScopeGrouping.Sections(rows, r => r.Group, Groups, Heading, "Other");

        Assert.Equal(["PLATFORM", "DIARY"], sections.Select(s => s.Heading));
    }

    [Fact]
    public void Ungrouped_ComesLast_UnderItsOwnHeading()
    {
        var rows = new[] { Row("misc:x"), Row("case:read", "diary") };

        var sections = ScopeGrouping.Sections(rows, r => r.Group, Groups, Heading, "Other");

        Assert.Equal(["DIARY", "Other"], sections.Select(s => s.Heading));
        Assert.Null(sections[^1].Key);
    }

    [Fact]
    public void AGroupWithNoRows_ShowsNoHeading()
    {
        var rows = new[] { Row("case:read", "diary") };

        var sections = ScopeGrouping.Sections(rows, r => r.Group, Groups, Heading, "Other");

        Assert.Equal(["diary"], sections.Select(s => s.Key));
    }

    [Fact]
    public void AGroupNeverDefined_IsShownUnderItsKey_AfterTheDefinedOnes()
    {
        var rows = new[] { Row("x:y", "zeta"), Row("case:read", "diary"), Row("a:b", "alpha") };

        var sections = ScopeGrouping.Sections(rows, r => r.Group, Groups, Heading, "Other");

        Assert.Equal(["diary", "alpha", "zeta"], sections.Select(s => s.Key));
    }

    [Fact]
    public void RowsKeepTheirOrderInsideAGroup()
    {
        var rows = new[] { Row("a", "diary"), Row("b", "platform"), Row("c", "diary") };

        var sections = ScopeGrouping.Sections(rows, r => r.Group, Groups, Heading, "Other");

        Assert.Equal(["a", "c"], sections.Single(s => s.Key == "diary").Items.Select(r => r.Name));
    }

    [Fact]
    public void OrderByGroup_FollowsGroupOrder_UngroupedLast_AndIsStable()
    {
        var rows = new[] { Row("u1"), Row("d1", "diary"), Row("p1", "platform"), Row("u2"), Row("d2", "diary") };

        var ordered = ScopeGrouping.OrderByGroup(rows, r => r.Group, Groups);

        Assert.Equal(["p1", "d1", "d2", "u1", "u2"], ordered.Select(r => r.Name));
    }

    [Fact]
    public void OrderByGroup_WithNoGroups_ChangesNothing()
    {
        var rows = new[] { Row("b"), Row("a") };

        Assert.Equal(["b", "a"], ScopeGrouping.OrderByGroup(rows, r => r.Group, []).Select(r => r.Name));
    }

    [Fact]
    public void BuildForRoles_CarriesTheGroupOntoTheRow()
    {
        var registry = new ScopeRegistry();
        registry.Register("case:read", AccessLevel.Viewer, "d", "diary");
        registry.Register("team:read", AccessLevel.Viewer);

        var rows = ScopeReference.BuildForRoles(registry, []);

        Assert.Equal("diary", rows.Single(r => r.Name == "case:read").Group);
        Assert.Null(rows.Single(r => r.Name == "team:read").Group);
    }
}
