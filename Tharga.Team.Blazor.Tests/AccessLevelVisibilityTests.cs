using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Which levels each selector offers once a host has hidden some, and the configurations that are refused.
/// </summary>
/// <remarks>
/// The rule these pin: <b>hiding narrows what can be chosen and nothing else.</b> Every assertion about a
/// selector is about its picker; none is about what the model accepts, because a hidden level stays valid
/// for members synced in from elsewhere (Tharga/Team#232).
/// </remarks>
public class AccessLevelVisibilityTests
{
    // The case the feature was built for.

    [Fact]
    public void HidingViewer_RemovesItFromEverySelector()
    {
        AccessLevel[] hidden = [AccessLevel.Viewer];

        Assert.Equal([AccessLevel.Administrator, AccessLevel.User],
            AccessLevelVisibility.Apply(AccessLevelVisibility.Member, hidden));
        Assert.Equal([AccessLevel.Administrator, AccessLevel.User, AccessLevel.Custom],
            AccessLevelVisibility.Apply(AccessLevelVisibility.ApiKey, hidden));
        Assert.Equal([AccessLevel.User, AccessLevel.Administrator],
            AccessLevelVisibility.Apply(AccessLevelVisibility.Consent, hidden));
    }

    /// <summary>
    /// The API-key selector is not the member selector, and hiding must not quietly merge them.
    /// <see cref="AccessLevel.Custom"/> is the least-privilege machine-key case that surface exists for.
    /// </summary>
    [Fact]
    public void HidingViewer_LeavesCustomOnTheApiKeySelector()
    {
        Assert.Contains(AccessLevel.Custom,
            AccessLevelVisibility.Apply(AccessLevelVisibility.ApiKey, [AccessLevel.Viewer]));
    }

    [Fact]
    public void NothingHidden_IsExactlyTheBuiltInSets()
    {
        Assert.Equal(AccessLevelVisibility.Member, AccessLevelVisibility.Apply(AccessLevelVisibility.Member, []));
        Assert.Equal(AccessLevelVisibility.ApiKey, AccessLevelVisibility.Apply(AccessLevelVisibility.ApiKey, []));
        Assert.Equal(AccessLevelVisibility.Consent, AccessLevelVisibility.Apply(AccessLevelVisibility.Consent, []));
    }

    [Fact]
    public void NullHidden_IsTreatedAsNothingHidden()
    {
        Assert.Equal(AccessLevelVisibility.Member, AccessLevelVisibility.Apply(AccessLevelVisibility.Member, null));
    }

    [Fact]
    public void Apply_PreservesOrder()
    {
        Assert.Equal([AccessLevel.Administrator, AccessLevel.Viewer],
            AccessLevelVisibility.Apply(AccessLevelVisibility.Member, [AccessLevel.User]));
    }

    [Fact]
    public void HidingALevelASelectorNeverOffered_ChangesNothing()
    {
        Assert.Equal(AccessLevelVisibility.Member,
            AccessLevelVisibility.Apply(AccessLevelVisibility.Member, [AccessLevel.Custom]));
    }

    // Refusals.

    /// <summary>
    /// Hiding Owner would change nothing, and someone writing it to mean "nobody may become Owner" has a
    /// security misunderstanding. Accepting it silently is what would let that belief survive.
    /// </summary>
    [Fact]
    public void HidingOwner_IsRefused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AccessLevelVisibility.Validate([AccessLevel.Owner]));

        Assert.Contains("TransferOwnershipAsync", ex.Message);
        Assert.Contains("SetOwnerAsync", ex.Message);
    }

    [Fact]
    public void HidingOwnerAlongsideAValidLevel_IsStillRefused()
    {
        Assert.Throws<InvalidOperationException>(
            () => AccessLevelVisibility.Validate([AccessLevel.Viewer, AccessLevel.Owner]));
    }

    [Fact]
    public void HidingEveryMemberLevel_IsRefused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AccessLevelVisibility.Validate(
            [AccessLevel.Administrator, AccessLevel.User, AccessLevel.Viewer]));

        Assert.Contains("inviting and editing team members", ex.Message);
    }

    /// <summary>
    /// The consent picker offers a narrower set than the API-key one, so it empties first — the message has
    /// to name the surface that actually broke rather than the first one checked.
    /// </summary>
    [Fact]
    public void HidingASetThatEmptiesOnlyOneSelector_NamesThatSelector()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AccessLevelVisibility.Validate(
            [AccessLevel.Administrator, AccessLevel.User, AccessLevel.Viewer, AccessLevel.Custom]));

        Assert.Contains("inviting and editing team members", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new AccessLevel[0])]
    public void NothingHidden_Validates(AccessLevel[] hidden)
    {
        AccessLevelVisibility.Validate(hidden);
    }

    /// <summary>
    /// Allowed, deliberately: the Owner still manages the team. It is the setting worth thinking hardest
    /// about, because management can then only be delegated by handing over ownership — but that is a
    /// coherent model, not a broken one, so it is documented rather than refused.
    /// </summary>
    [Fact]
    public void HidingAdministrator_IsAllowed()
    {
        AccessLevelVisibility.Validate([AccessLevel.Administrator]);

        Assert.Equal([AccessLevel.User, AccessLevel.Viewer],
            AccessLevelVisibility.Apply(AccessLevelVisibility.Member, [AccessLevel.Administrator]));
    }

    // Registration.

    [Fact]
    public void AddThargaTeamBlazor_WithAnInvalidConfiguration_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(
            () => services.AddThargaTeamBlazor(o => o.HiddenAccessLevels = [AccessLevel.Owner]));
    }

    [Fact]
    public void AddThargaTeamBlazor_WithAValidConfiguration_DoesNotThrow()
    {
        var services = new ServiceCollection();

        var exception = Record.Exception(() => services.AddThargaTeamBlazor(o => o.HiddenAccessLevels = [AccessLevel.Viewer]));

        Assert.Null(exception);
    }

    [Fact]
    public void AddThargaTeamBlazor_DefaultsToHidingNothing()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamBlazor();

        var options = services.BuildServiceProvider()
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<ThargaBlazorOptions>>();

        Assert.Empty(options.Value.HiddenAccessLevels);
    }
}
