namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The site's icon settings: one document, a fixed id so saving twice replaces rather than accumulates, and a
/// round trip that does not quietly drop a value.
/// </summary>
public class IconSettingsStoreTests
{
    private readonly IIconSettingsRepositoryCollection _collection = Substitute.For<IIconSettingsRepositoryCollection>();

    private MongoIconSettingsStore Sut() => new(_collection);

    /// <summary>Nothing saved means nothing stored — which is what leaves startup configuration in force.</summary>
    [Fact]
    public async Task Load_WithNothingStored_ReturnsNull()
    {
        Assert.Null(await Sut().LoadAsync());
    }

    /// <remarks>
    /// The id is fixed rather than generated, so two saves cannot create two "only" documents. A generated id
    /// would leave the unique index on Key as the sole guard, and it would fail the second save rather than
    /// replace the first.
    /// </remarks>
    [Fact]
    public async Task Save_WritesTheOneDocument_AtItsFixedIdentity()
    {
        IconSettingsEntity written = null;
        await _collection.AddOrReplaceAsync(Arg.Do<IconSettingsEntity>(e => written = e));

        await Sut().SaveAsync(new IconSettingsState { GravatarEnabled = false, GravatarStyle = GravatarStyles.RoboHash });

        Assert.Equal(IconSettingsEntity.SingletonKey, written.Key);
        Assert.Equal(MongoIconSettingsStore.SingletonId, written.Id);
        Assert.False(written.GravatarEnabled);
        Assert.Equal(GravatarStyles.RoboHash, written.GravatarStyle);
        Assert.NotEqual(default, written.ChangedUtc);
    }

    /// <summary>A blank style is stored as the default, so nothing reads back a <c>d=</c> with no value.</summary>
    [Fact]
    public async Task Save_BlankStyle_StoresTheDefault()
    {
        IconSettingsEntity written = null;
        await _collection.AddOrReplaceAsync(Arg.Do<IconSettingsEntity>(e => written = e));

        await Sut().SaveAsync(new IconSettingsState { GravatarStyle = "  " });

        Assert.Equal(GravatarStyles.Default, written.GravatarStyle);
    }

    /// <summary>Every value survives the trip out to storage and back.</summary>
    /// <remarks>
    /// Asserted field by field on purpose: the failure this catches is a property added to
    /// <see cref="IconSettingsState"/> and mapped in one direction only, which loses a setting silently on the
    /// next restart rather than at the moment somebody changes it.
    /// </remarks>
    [Fact]
    public async Task SaveThenLoad_KeepsEveryValue()
    {
        var original = new IconSettingsState
        {
            GravatarEnabled = false,
            GravatarStyle = GravatarStyles.Wavatar,
            DefaultUserIconUrl = "https://example.com/a.png",
            AllowUserUpload = false,
            AllowAdminUpload = false
        };

        IconSettingsEntity written = null;
        await _collection.AddOrReplaceAsync(Arg.Do<IconSettingsEntity>(e => written = e));
        await Sut().SaveAsync(original);

        _collection.GetOneAsync(Arg.Any<System.Linq.Expressions.Expression<Func<IconSettingsEntity, bool>>>())
            .Returns(written);

        var loaded = await Sut().LoadAsync();

        Assert.Equal(original.GravatarEnabled, loaded.GravatarEnabled);
        Assert.Equal(original.GravatarStyle, loaded.GravatarStyle);
        Assert.Equal(original.DefaultUserIconUrl, loaded.DefaultUserIconUrl);
        Assert.Equal(original.AllowUserUpload, loaded.AllowUserUpload);
        Assert.Equal(original.AllowAdminUpload, loaded.AllowAdminUpload);
    }

    /// <summary>Applying stored values onto the live singleton carries all of them, and repairs a blank style.</summary>
    [Fact]
    public void ApplyTo_WritesEveryValueOntoTheLiveSettings()
    {
        var live = new IconSettings();
        new IconSettingsState
        {
            GravatarEnabled = false,
            GravatarStyle = null,
            DefaultUserIconUrl = "https://example.com/b.png",
            AllowUserUpload = false,
            AllowAdminUpload = false
        }.ApplyTo(live);

        Assert.False(live.GravatarEnabled);
        Assert.Equal(GravatarStyles.Default, live.GravatarStyle);
        Assert.Equal("https://example.com/b.png", live.DefaultUserIconUrl);
        Assert.False(live.AllowUserUpload);
        Assert.False(live.AllowAdminUpload);

        var round = IconSettingsState.From(live);
        Assert.Equal(live.GravatarEnabled, round.GravatarEnabled);
        Assert.Equal(live.GravatarStyle, round.GravatarStyle);
        Assert.Equal(live.DefaultUserIconUrl, round.DefaultUserIconUrl);
        Assert.Equal(live.AllowUserUpload, round.AllowUserUpload);
        Assert.Equal(live.AllowAdminUpload, round.AllowAdminUpload);
    }
}
