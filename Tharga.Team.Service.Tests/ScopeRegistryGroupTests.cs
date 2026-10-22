using Tharga.Team;

namespace Tharga.Team.Service.Tests;

public class ScopeRegistryGroupTests
{
    [Fact]
    public void NoGroupsDefined_GroupsIsEmpty_AndScopesAreUngrouped()
    {
        var registry = new ScopeRegistry();
        registry.Register("doc:read", AccessLevel.Viewer);

        Assert.Empty(registry.Groups);
        Assert.Null(registry.All.Single().Group);
    }

    [Fact]
    public void Groups_AreReturnedInTheOrderTheyWereDefined()
    {
        var registry = new ScopeRegistry();
        registry.DefineGroup("platform", "Platform");
        registry.DefineGroup("diary", "Diary");

        Assert.Equal(["platform", "diary"], registry.Groups.Select(g => g.Key));
        Assert.Equal("Diary", registry.Groups[1].DisplayName);
    }

    [Fact]
    public void Register_WithGroup_StoresTheGroup()
    {
        var registry = new ScopeRegistry();
        registry.Register("case:read", AccessLevel.Viewer, "Read cases", group: "diary");

        var scope = registry.All.Single();
        Assert.Equal("diary", scope.Group);
        Assert.False(scope.GrantOnly);
        Assert.Equal("Read cases", scope.Description);
    }

    [Fact]
    public void RegisterGrantOnly_WithGroup_StoresTheGroup_AndStaysGrantOnly()
    {
        var registry = new ScopeRegistry();
        registry.RegisterGrantOnly("record:erase", "Erase records", group: "archive");

        var scope = registry.All.Single();
        Assert.Equal("archive", scope.Group);
        Assert.True(scope.GrantOnly);
        Assert.Empty(registry.GetScopesForAccessLevel(AccessLevel.Owner));
    }

    [Fact]
    public void Group_DoesNotChangeWhoIsGranted()
    {
        var grouped = new ScopeRegistry();
        grouped.Register("a:read", AccessLevel.Viewer, null, "g");
        grouped.Register("a:write", AccessLevel.User, null, "g");
        var plain = new ScopeRegistry();
        plain.Register("a:read", AccessLevel.Viewer);
        plain.Register("a:write", AccessLevel.User);

        foreach (var level in new[] { AccessLevel.Owner, AccessLevel.Administrator, AccessLevel.User, AccessLevel.Viewer, AccessLevel.Custom })
            Assert.Equal(plain.GetScopesForAccessLevel(level), grouped.GetScopesForAccessLevel(level));
    }

    [Fact]
    public void DefineGroup_Twice_Throws()
    {
        var registry = new ScopeRegistry();
        registry.DefineGroup("diary", "Diary");

        Assert.Throws<InvalidOperationException>(() => registry.DefineGroup("diary", "Other"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void DefineGroup_WithoutKey_Throws(string key)
    {
        Assert.Throws<ArgumentException>(() => new ScopeRegistry().DefineGroup(key, "Name"));
    }

    [Fact]
    public void DefineGroup_WithoutDisplayName_UsesTheKey()
    {
        var registry = new ScopeRegistry();
        registry.DefineGroup("diary", null);

        Assert.Equal("diary", registry.Groups.Single().DisplayName);
    }

    [Fact]
    public void ScopeDefinition_KeepsItsOriginalConstructorAndNamedArguments()
    {
        var definition = new ScopeDefinition("a:read", AccessLevel.Viewer, "d", GrantOnly: true);

        Assert.True(definition.GrantOnly);
        Assert.Null(definition.Group);
    }

    [Fact]
    public void TextKeyFor_IsStablePerGroup()
    {
        Assert.Equal("team.scopeGroup.diary", ScopeGroup.TextKeyFor("diary"));
    }
}
