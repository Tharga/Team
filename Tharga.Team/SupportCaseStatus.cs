namespace Tharga.Team;

/// <summary>
/// Where a support case is in its life.
/// </summary>
/// <remarks>
/// Deliberately two values. A richer set (pending, waiting-on-customer, escalated) is a support-desk
/// workflow, and inventing one before a desk exists is how a status field becomes a field nobody agrees on.
/// Add values when a channel or a workflow needs them.
/// </remarks>
public enum SupportCaseStatus
{
    Open,
    Closed
}
