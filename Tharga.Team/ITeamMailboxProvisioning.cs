using System.ComponentModel;

namespace Tharga.Team;

/// <summary>
/// Gives a team its mail address without a caller's grant — for host code that decides on its own authority that
/// a team should have one, such as a feature being switched on.
/// </summary>
/// <remarks>
/// <b>An internal service: never inject it from a component, controller or MCP provider.</b> It checks nothing,
/// so a host calls it only behind its own check — showing a case's address to whoever may read the case, say —
/// and everything a person does goes through <see cref="ITeamMailboxService"/>, where the scopes are.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ITeamMailboxProvisioning
{
    /// <summary>The team's addresses, issuing its first key when it has none.</summary>
    Task<TeamMailAddresses> EnsureKeyAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>The address of an item inside the team on its primary key, issuing the team's first key when it has none.</summary>
    Task<string> EnsureItemAddressAsync(string teamKey, string itemKey, CancellationToken cancellationToken = default);
}
