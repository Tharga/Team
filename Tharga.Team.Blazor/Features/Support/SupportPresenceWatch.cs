namespace Tharga.Team.Blazor.Features.Support;

/// <summary>
/// Whether anybody is on support, read when asked and again each time <see cref="ISupportPresence.Changed"/> says so.
/// </summary>
/// <remarks>
/// Advisory: anything that goes wrong reading it is <see cref="SupportPresenceState.Unknown"/>, which shows nothing.
/// </remarks>
internal sealed class SupportPresenceWatch : IDisposable
{
    private readonly ISupportPresence _presence;
    private readonly Func<Func<Task>, Task> _invoke;
    private readonly Action _changed;

    /// <param name="presence">Null when the host has no presence; it is then always unknown.</param>
    /// <param name="invoke">The component's <c>InvokeAsync</c>, since a change is raised on whatever thread made it.</param>
    /// <param name="changed">Called on the component's thread after a change was read.</param>
    public SupportPresenceWatch(ISupportPresence presence, Func<Func<Task>, Task> invoke, Action changed)
    {
        _presence = presence;
        _invoke = invoke;
        _changed = changed;

        if (_presence != null) _presence.Changed += OnChanged;
    }

    /// <summary>What was read last.</summary>
    public SupportPresenceState State { get; private set; } = SupportPresenceState.Unknown;

    /// <summary>Reads it again.</summary>
    public async Task ReadAsync()
    {
        try
        {
            State = _presence == null ? SupportPresenceState.Unknown : await _presence.GetAsync();
        }
        catch (Exception)
        {
            State = SupportPresenceState.Unknown;
        }
    }

    private void OnChanged(object sender, EventArgs args) => _ = _invoke(async () =>
    {
        await ReadAsync();
        _changed?.Invoke();
    });

    public void Dispose()
    {
        if (_presence != null) _presence.Changed -= OnChanged;
    }
}
