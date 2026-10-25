namespace Tharga.Team;

/// <summary>
/// Whether anybody is on support right now.
/// </summary>
/// <remarks>
/// <b>Advisory, and never a gate.</b> It answers "is somebody likely to see this soon", which changes what a
/// customer expects — not whether they may ask. Nothing on the path that raises a case may wait on this or
/// be refused by it.
/// <para>
/// <b>Two sources</b>: the support desk, where each support person sets a <see cref="SupportStaffStatus"/>, and
/// Slack, where a support channel is configured. Online when either says so. Registered with the support cases;
/// resolve it with <c>GetService</c> rather than <c>GetRequiredService</c> all the same, because absent means the
/// same thing as <see cref="SupportPresenceState.Unknown"/> — say nothing.
/// </para>
/// <para>
/// <b>It lives here, with the contracts, rather than in the support package</b> for the same reason
/// <see cref="Support.Cases.ISupportCaseService"/> does: a component has to be able to reach it without
/// <c>Tharga.Team.Blazor</c> taking a dependency on a package that carries a mail stack.
/// </para>
/// </remarks>
public interface ISupportPresence
{
    /// <summary>
    /// Whether support is reachable, as far as can be told.
    /// </summary>
    /// <remarks>
    /// Never throws and never blocks meaningfully: a transport failure, a rate limit or a missing
    /// configuration all come back as <see cref="SupportPresenceState.Unknown"/>.
    /// </remarks>
    Task<SupportPresenceState> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when the answer may have changed, so a page showing it asks again.
    /// </summary>
    /// <remarks>
    /// Raised on whichever thread made the change: marshal with <c>InvokeAsync</c>, and unsubscribe in <c>Dispose</c>.
    /// The toolkit's own raises it when somebody on the support desk sets a status, goes idle or comes back; Slack's
    /// answer is read again when a page next asks. An implementation of your own need not raise it.
    /// </remarks>
    event EventHandler Changed
    {
        add { }
        remove { }
    }
}

/// <summary>
/// What is known about support being reachable.
/// </summary>
/// <remarks>
/// <b><see cref="Unknown"/> is a real answer and must render as nothing</b>, not as "offline". Telling
/// somebody not to bother when support is in fact there is worse than saying nothing at all — and unknown is
/// what a rate limit, a network blip or an unconfigured workspace all produce.
/// </remarks>
public enum SupportPresenceState
{
    /// <summary>Cannot be determined. Render nothing.</summary>
    Unknown,

    /// <summary>Nobody on the support channel is active.</summary>
    Away,

    /// <summary>At least one person on the support channel is active.</summary>
    Online
}
