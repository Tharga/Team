namespace Tharga.Team.Support.Cases;

/// <summary>
/// Whether anybody is on support: the support desk, and Slack where a support channel is configured.
/// </summary>
/// <remarks>
/// Online when either says so, since one person there to answer is the whole answer. Away when either knows of support
/// and neither has anybody online. Unknown only when neither can tell, which renders as nothing. Slack is asked only
/// when the desk has nobody online, and its own cache keeps that to one call a minute.
/// </remarks>
internal sealed class SupportPresence(SupportDeskPresence desk, SlackSupportPresence slack = null) : ISupportPresence
{
    public async Task<SupportPresenceState> GetAsync(CancellationToken cancellationToken = default)
    {
        var fromDesk = desk.State;
        if (fromDesk == SupportPresenceState.Online || slack == null) return fromDesk;

        var fromSlack = await slack.GetAsync(cancellationToken);

        if (fromSlack == SupportPresenceState.Online) return SupportPresenceState.Online;

        return fromDesk == SupportPresenceState.Away || fromSlack == SupportPresenceState.Away
            ? SupportPresenceState.Away
            : SupportPresenceState.Unknown;
    }

    public event EventHandler Changed
    {
        add => desk.Changed += value;
        remove => desk.Changed -= value;
    }
}
