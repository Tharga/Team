using System.Security.Claims;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The site icon-settings service: what it stores, what it applies, and what it refuses.
/// </summary>
public class IconSettingsServiceTests
{
    private sealed class RecordingStore : IIconSettingsStore
    {
        public IconSettingsState Saved { get; private set; }
        public Exception Fails { get; init; }

        public Task<IconSettingsState> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Saved);

        public Task SaveAsync(IconSettingsState settings, CancellationToken cancellationToken = default)
        {
            if (Fails != null) return Task.FromException(Fails);
            Saved = settings;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Save_StoresAndAppliesImmediately()
    {
        var live = new IconSettings { GravatarStyle = GravatarStyles.Identicon };
        var store = new RecordingStore();

        await new IconSettingsService(live, store).SaveAsync(new IconSettingsState { GravatarStyle = GravatarStyles.Retro, GravatarEnabled = false });

        Assert.Equal(GravatarStyles.Retro, store.Saved.GravatarStyle);
        Assert.Equal(GravatarStyles.Retro, live.GravatarStyle);
        Assert.False(live.GravatarEnabled);
    }

    /// <summary>
    /// A failed write leaves the instance showing what is actually stored, rather than a value only it believes.
    /// </summary>
    [Fact]
    public async Task Save_WhenTheStoreFails_DoesNotApplyLocally()
    {
        var live = new IconSettings { GravatarStyle = GravatarStyles.Identicon };
        var store = new RecordingStore { Fails = new InvalidOperationException("no connection") };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new IconSettingsService(live, store).SaveAsync(new IconSettingsState { GravatarStyle = GravatarStyles.Retro }));

        Assert.Equal(GravatarStyles.Identicon, live.GravatarStyle);
    }

    /// <summary>Without a store the knob still works; it simply does not survive a restart.</summary>
    [Fact]
    public async Task Save_WithNoStore_StillAppliesLocally()
    {
        var live = new IconSettings { GravatarStyle = GravatarStyles.Identicon };

        await new IconSettingsService(live).SaveAsync(new IconSettingsState { GravatarStyle = GravatarStyles.Wavatar });

        Assert.Equal(GravatarStyles.Wavatar, live.GravatarStyle);
    }

    /// <summary>
    /// An unknown style is refused rather than stored.
    /// </summary>
    /// <remarks>
    /// The value ends up in a URL Gravatar interprets, so a typo does not fail anywhere — it silently serves
    /// the wrong default image to everybody. Refusing at the boundary is what makes that impossible.
    /// </remarks>
    [Fact]
    public async Task Save_UnknownStyle_IsRefused()
    {
        var live = new IconSettings();
        var store = new RecordingStore();

        var e = await Assert.ThrowsAsync<ArgumentException>(
            () => new IconSettingsService(live, store).SaveAsync(new IconSettingsState { GravatarStyle = "identicorn" }));

        Assert.Contains("identicorn", e.Message);
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task Get_ReadsTheLiveValues()
    {
        var live = new IconSettings { GravatarStyle = GravatarStyles.MonsterId, AllowAdminUpload = false };

        var state = await new IconSettingsService(live).GetAsync();

        Assert.Equal(GravatarStyles.MonsterId, state.GravatarStyle);
        Assert.False(state.AllowAdminUpload);
    }

    /// <summary>
    /// Both members declare the system <c>users:manage</c> scope, so the proxy cannot let an unattributed one
    /// through.
    /// </summary>
    /// <remarks>
    /// Asserted by reflection rather than by naming the members, so a method added later is covered without
    /// anyone remembering to extend this. The proxy fails closed on an unattributed method — this is what
    /// makes that failure impossible to reach in the first place.
    /// </remarks>
    [Fact]
    public void EveryMember_RequiresTheSystemUserManageScope()
    {
        var members = typeof(IIconSettingsService).GetMethods();

        Assert.NotEmpty(members);
        Assert.All(members, m =>
        {
            var attribute = m.GetCustomAttributes(typeof(RequireScopeAttribute), true).Cast<RequireScopeAttribute>().SingleOrDefault();
            Assert.NotNull(attribute);
            Assert.Equal(SystemUserScopes.Manage, attribute.Scope);
        });
    }
}
