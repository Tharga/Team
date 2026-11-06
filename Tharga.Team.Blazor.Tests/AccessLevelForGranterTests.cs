using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The pickers offer only the levels the signed-in caller may hand out (Tharga/Team#409). A convenience: the
/// services refuse the rest whatever is offered.
/// </summary>
public class AccessLevelForGranterTests
{
    [Theory]
    [InlineData(AccessLevel.Owner)]
    [InlineData(AccessLevel.Administrator)]
    public void AnOwnerOrAdministrator_IsOfferedEverything(AccessLevel granter)
        => Assert.Equal(AccessLevelVisibility.ApiKey, AccessLevelVisibility.ForGranter(AccessLevelVisibility.ApiKey, granter));

    [Theory]
    [InlineData(AccessLevel.User)]
    [InlineData(AccessLevel.Viewer)]
    [InlineData(AccessLevel.Custom)]
    [InlineData(null)]
    public void Anyone_Else_IsNotOfferedAdministrator(AccessLevel? granter)
        => Assert.Equal([AccessLevel.User, AccessLevel.Viewer, AccessLevel.Custom], AccessLevelVisibility.ForGranter(AccessLevelVisibility.ApiKey, granter));

    [Fact]
    public void AnExemptCaller_IsOfferedEverything()
        => Assert.Equal(AccessLevelVisibility.ApiKey, AccessLevelVisibility.ForGranter(AccessLevelVisibility.ApiKey, AccessLevel.Viewer, exempt: true));
}
