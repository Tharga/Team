using System.Reflection;
using Microsoft.AspNetCore.Components;
using Tharga.Team;
using Tharga.Team.Blazor.Features.User;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Opening <c>UsersView</c> on a given tab and row from a link (Tharga/Team#330).
/// </summary>
public class UsersViewTabTests
{
    [Theory]
    [InlineData(UsersViewTab.Users, true, 0)]
    [InlineData(UsersViewTab.Teams, true, 1)]
    [InlineData(UsersViewTab.Directory, true, 2)]
    [InlineData(UsersViewTab.Teams, false, 1)]
    [InlineData(UsersViewTab.Directory, false, 0)] // no Directory tab: fall back to Users, never a missing index
    public void ToIndex_MapsTheTab(UsersViewTab tab, bool showDirectoryTab, int expected)
    {
        Assert.Equal(expected, UsersViewTabs.ToIndex(tab, showDirectoryTab));
    }

    [Theory]
    [InlineData(0, true, UsersViewTab.Users)]
    [InlineData(1, true, UsersViewTab.Teams)]
    [InlineData(2, true, UsersViewTab.Directory)]
    [InlineData(2, false, UsersViewTab.Users)]
    [InlineData(-1, true, UsersViewTab.Users)]
    public void FromIndex_MapsTheIndex(int index, bool showDirectoryTab, UsersViewTab expected)
    {
        Assert.Equal(expected, UsersViewTabs.FromIndex(index, showDirectoryTab));
    }

    [Theory]
    [InlineData("SelectedTab", typeof(UsersViewTab))]
    [InlineData("SelectedTabChanged", typeof(EventCallback<UsersViewTab>))]
    [InlineData("FocusTeamKey", typeof(string))]
    [InlineData("FocusUserKey", typeof(string))]
    public void UsersView_ExposesTheLinkParameters(string name, Type type)
    {
        var view = typeof(UsersView<>).MakeGenericType(typeof(FakeMember));
        var prop = view.GetProperty(name);

        Assert.NotNull(prop);
        Assert.Equal(type, prop.PropertyType);
        Assert.NotNull(prop.GetCustomAttribute<ParameterAttribute>());
    }

    [Fact]
    public void UsersView_OpensOnUsers_ByDefault()
    {
        var view = Activator.CreateInstance(typeof(UsersView<>).MakeGenericType(typeof(FakeMember)));

        Assert.Equal(UsersViewTab.Users, view.GetType().GetProperty("SelectedTab").GetValue(view));
    }

    private sealed record FakeMember(string Key) : ITeamMember
    {
        public string Name { get; init; }
        public AccessLevel? AccessLevel => Tharga.Team.AccessLevel.Viewer;
        public string[] TenantRoles => [];
        public string[] ScopeOverrides => [];
        public Invitation Invitation => null;
        public DateTime? LastSeen => null;
        public MembershipState? State => MembershipState.Member;
    }
}
