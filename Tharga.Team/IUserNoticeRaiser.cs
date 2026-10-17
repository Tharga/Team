using System.ComponentModel;

namespace Tharga.Team;

/// <summary>Raises automatic notices from server code; unchecked, so call it from code acting on its own authority, never from a component, controller or MCP provider.</summary>
/// <remarks>
/// <b>Cheap and never waits on mail.</b> Raising checks the occurrence against the catalogue and stores it, which
/// survives a restart and is picked up by whichever instance claims it; finding the recipients, applying their choices
/// and queuing the mail happen in the background.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IUserNoticeRaiser
{
    /// <summary>Checks and stores the occurrence for delivery, returning its id.</summary>
    /// <exception cref="ArgumentException">The notice is not in the catalogue, a required field is missing, a placeholder value is missing or not declared, or the link is not a site path.</exception>
    Task<string> RaiseAsync(UserNoticeOccurrence occurrence, CancellationToken cancellationToken = default);

    /// <summary>What became of a raised occurrence, by the id <see cref="RaiseAsync"/> returned, or null when there is none.</summary>
    Task<UserNoticeRecord> GetAsync(string id, CancellationToken cancellationToken = default);
}
