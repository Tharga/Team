namespace Tharga.Team;

/// <summary>
/// Keeps teams' mail keys. The persistence port behind <see cref="ITeamMailboxService"/>.
/// </summary>
/// <remarks>
/// <b>The port expresses atomicity.</b> Rotating changes three keys at once — a new primary, the old primary
/// demoted, an older secondary retired — so it is one operation an adapter performs as one write, never three
/// calls a caller sequences. <see cref="IssueAsync"/> refuses a second primary for the same reason: two
/// instances issuing a team's first key at the same moment must end with one key, not two.
/// <para>
/// Retired keys are kept. A mail to a retired key goes to the postmaster as retired rather than unknown, which
/// tells whoever reads it that the sender is still using an old address.
/// </para>
/// </remarks>
public interface ITeamMailKeyStore
{
    /// <summary>Every key the team has held, retired ones included.</summary>
    Task<TeamMailKeyRecord[]> GetForTeamAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>The key with this value, whichever team holds it; null when nobody does.</summary>
    Task<TeamMailKeyRecord> FindAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Adds a team's first primary key; false, and nothing written, when the team already has a primary.</summary>
    Task<bool> IssueAsync(TeamMailKeyRecord primary, CancellationToken cancellationToken = default);

    /// <summary>
    /// In one write: retires the team's current secondary at <paramref name="now"/>, makes its primary the
    /// secondary until <paramref name="retiresAt"/>, and adds <paramref name="newPrimary"/>.
    /// </summary>
    Task RotateAsync(string teamKey, TeamMailKeyRecord newPrimary, DateTime retiresAt, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Removes every key the team has held, when the team itself is removed.</summary>
    Task DeleteForTeamAsync(string teamKey, CancellationToken cancellationToken = default);
}
