using Tharga.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// How <c>ApiKeyView</c> follows <see cref="ApiKeyOptions.RequirePrivateKeys"/> (Tharga/Team#314).
/// </summary>
public class ApiKeyPrivacyTests
{
    /// <summary>
    /// The one that is easy to miss: on the default <c>None</c>, a page would create a private key and then
    /// not list it — the key would seem to vanish the moment it was made.
    /// </summary>
    [Fact]
    public void Required_None_ListsTheCallersOwn()
    {
        Assert.Equal(PrivateKeyScope.Mine, ApiKeyPrivacy.Listed(PrivateKeyScope.None, requirePrivateKeys: true));
    }

    [Theory]
    [InlineData(PrivateKeyScope.Mine)]
    [InlineData(PrivateKeyScope.All)]
    public void Required_AWiderChoice_IsKept(PrivateKeyScope shown)
    {
        Assert.Equal(shown, ApiKeyPrivacy.Listed(shown, requirePrivateKeys: true));
    }

    [Theory]
    [InlineData(PrivateKeyScope.None)]
    [InlineData(PrivateKeyScope.Mine)]
    [InlineData(PrivateKeyScope.All)]
    public void NotRequired_ListsWhatThePageAsked(PrivateKeyScope shown)
    {
        Assert.Equal(shown, ApiKeyPrivacy.Listed(shown, requirePrivateKeys: false));
    }

    [Fact]
    public void Required_TheSwitchIsShown_EvenOnNone()
    {
        Assert.True(ApiKeyPrivacy.ShowSwitch(PrivateKeyScope.None, requirePrivateKeys: true));
    }

    [Fact]
    public void NotRequired_None_HidesTheSwitchAsBefore()
    {
        Assert.False(ApiKeyPrivacy.ShowSwitch(PrivateKeyScope.None, requirePrivateKeys: false));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    public void CreatesPrivate_IsTheChoice_OrAlwaysWhenRequired(bool chosen, bool required, bool expected)
    {
        Assert.Equal(expected, ApiKeyPrivacy.CreatesPrivate(chosen, required));
    }
}
