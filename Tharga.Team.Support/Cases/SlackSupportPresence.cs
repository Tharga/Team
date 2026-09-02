using Microsoft.Extensions.Options;
using Tharga.Team.Support.Slack;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Answers "is anybody on support" from who is active on the support channel.
/// </summary>
/// <remarks>
/// <b>Membership of the channel defines support</b>, so adding somebody to it is how they become support and
/// there is no second list to drift. That was the decision (user, 2026-09-02) over a configured list of user
/// ids or a Slack user group — the group would have been tidier and is a paid-plan feature.
/// <para>
/// <b>Two caches with different lifetimes, because the two questions change at different rates.</b> Who is in
/// the channel changes when somebody joins a team; whether they are active changes minute to minute. One TTL
/// for both would either hammer <c>conversations.members</c> or answer presence from a stale roster.
/// </para>
/// <para>
/// <b>The cache is process-local, and unlike <see cref="Tharga.Team.ISupportEventLedger"/> that is fine.</b> A
/// process-local ledger would be a correctness defect — two instances would both accept the same retry. Here
/// the only cost of N instances is N times the API calls, still bounded by the TTL, and a wrong answer is
/// already handled: presence is advisory. Sharing it would need a backplane that does not exist, to save
/// calls that are not a problem.
/// </para>
/// <para>
/// <b>Never throws and never reports "away" when it means "cannot tell".</b> Every failure path returns
/// <see cref="SupportPresenceState.Unknown"/>, which a caller renders as nothing.
/// </para>
/// </remarks>
internal sealed class SlackSupportPresence(
    ISlackClient slackClient,
    IOptions<SupportCaseOptions> options,
    TimeProvider timeProvider) : ISupportPresence
{
    /// <summary>How long the channel roster is trusted. Joining a team is not a minute-to-minute event.</summary>
    private static readonly TimeSpan MembersTtl = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long an answer about who is active is trusted.
    /// </summary>
    /// <remarks>
    /// Long enough that a page rendering repeatedly costs one call, short enough that somebody signing on is
    /// noticed while the customer is still deciding whether to wait.
    /// </remarks>
    private static readonly TimeSpan PresenceTtl = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _gate = new(1, 1);

    private string[] _members = [];
    private DateTimeOffset _membersAt = DateTimeOffset.MinValue;

    private SupportPresenceState _state = SupportPresenceState.Unknown;
    private DateTimeOffset _stateAt = DateTimeOffset.MinValue;

    public async Task<SupportPresenceState> GetAsync(CancellationToken cancellationToken = default)
    {
        var channel = options.Value.SlackChannel;

        if (string.IsNullOrWhiteSpace(channel)) return SupportPresenceState.Unknown;

        var now = timeProvider.GetUtcNow();

        if (now - _stateAt < PresenceTtl) return _state;

        // One caller refreshes; the rest take the answer it produced. Without this, a page that renders for
        // twenty people at once asks Slack about the whole channel twenty times over.
        await _gate.WaitAsync(cancellationToken);
        try
        {
            now = timeProvider.GetUtcNow();
            if (now - _stateAt < PresenceTtl) return _state;

            _state = await ResolveAsync(channel, now, cancellationToken);
            _stateAt = now;

            return _state;
        }
        catch (Exception)
        {
            return SupportPresenceState.Unknown;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SupportPresenceState> ResolveAsync(string channel, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (now - _membersAt >= MembersTtl)
        {
            var members = await slackClient.GetChannelMembersAsync(channel, cancellationToken);

            // An empty read is a failure, not an empty channel — keep the roster we had rather than
            // concluding that support has nobody in it.
            if (members.Length > 0)
            {
                _members = members;
                _membersAt = now;
            }
        }

        if (_members.Length == 0) return SupportPresenceState.Unknown;

        var known = false;

        foreach (var member in _members)
        {
            var active = await slackClient.IsActiveAsync(member, cancellationToken);

            // One active member is the whole answer, so stop asking about the rest.
            if (active == true) return SupportPresenceState.Online;

            known |= active == false;
        }

        // Away only when somebody actually said so. All-unknown means the workspace would not tell us, and
        // that must not read as "nobody is there".
        return known ? SupportPresenceState.Away : SupportPresenceState.Unknown;
    }
}
