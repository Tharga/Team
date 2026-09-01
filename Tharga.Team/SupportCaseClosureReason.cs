namespace Tharga.Team;

/// <summary>
/// Why a support case is closed.
/// </summary>
/// <remarks>
/// A component has to render the two differently — "you closed this" and "this closed itself because nobody
/// came back" are not the same message to put in front of a customer, and the second needs to say that
/// reopening is available.
/// </remarks>
public enum SupportCaseClosureReason
{
    /// <summary>Somebody closed it.</summary>
    Manual,

    /// <summary>It closed itself: support answered and nobody came back within the configured span.</summary>
    Inactivity
}
