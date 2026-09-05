namespace Tharga.Team;

public record Invitation
{
    public required string EMail { get; init; }
    public required string InviteKey { get; init; }
    public required DateTime InviteTime { get; init; }

    /// <summary>
    /// When this invitation stops being acceptable, or <c>null</c> to fall back to the configured
    /// <see cref="InvitationOptions.Lifetime"/> measured from <see cref="InviteTime"/>.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately not <c>required</c>, unlike the three above.</b> Hosts construct an
    /// <see cref="Invitation"/> themselves — overriding member creation to generate the code and stamp the
    /// time is a supported thing to do — so a required member here would be a compile break for them rather
    /// than a new capability.
    /// <para>
    /// <b>Why the expiry lives on the record rather than being derived.</b> A lifetime alone answers "has
    /// this expired", but not "give this one another fortnight" — extending would mean rewriting
    /// <see cref="InviteTime"/>, which would falsify when the invitation was created. Holding the expiry
    /// separately is what lets an invitation be extended while <see cref="InviteKey"/> stays the same, so a
    /// link that has already been mailed keeps working.
    /// </para>
    /// <para>
    /// Absent on invitations created before this existed, which is exactly the null case: they fall back to
    /// the configured lifetime, and where none is configured they never expire — the behaviour they had.
    /// </para>
    /// </remarks>
    public DateTime? ExpiresAt { get; init; }
}
