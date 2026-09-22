namespace Tharga.Team;

/// <summary>
/// The built-in <see cref="IIconSettingsService"/>: the live singleton is the read, the store is the record.
/// </summary>
/// <remarks>
/// Without an <see cref="IIconSettingsStore"/> the settings are still readable and still changeable — they
/// simply do not survive a restart. That is the right behaviour for a host that registered no store: the
/// runtime knob keeps working, and nothing pretends to persist.
/// </remarks>
public sealed class IconSettingsService : IIconSettingsService
{
    private readonly IconSettings _settings;
    private readonly IIconSettingsStore _store;

    public IconSettingsService(IconSettings settings, IIconSettingsStore store = null)
    {
        _settings = settings;
        _store = store;
    }

    public Task<IconSettingsState> GetAsync() => Task.FromResult(IconSettingsState.From(_settings));

    public async Task SaveAsync(IconSettingsState settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!GravatarStyles.IsKnown(settings.GravatarStyle))
            throw new ArgumentException($"'{settings.GravatarStyle}' is not a Gravatar style. One of: {string.Join(", ", GravatarStyles.All)}.", nameof(settings));

        if (_store != null) await _store.SaveAsync(settings);

        // Applied after the write, so a failed save leaves the instance showing what is actually stored.
        settings.ApplyTo(_settings);
    }
}
