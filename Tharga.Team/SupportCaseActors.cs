namespace Tharga.Team;

/// <summary>
/// Identities the toolkit itself acts under on a support case.
/// </summary>
/// <remarks>
/// <b>The toolkit is a legitimate actor on a case</b>, and saying so in the same field as everyone else is
/// what keeps the transcript readable: a closure with no actor reads as a gap, and inventing a second
/// "who did this" field to hold "nobody" would mean two places to check.
/// <para>
/// The <c>system:</c> prefix is what keeps these from colliding with a real authentication subject.
/// </para>
/// </remarks>
public static class SupportCaseActors
{
    /// <summary>
    /// Recorded as <see cref="SupportCase.ClosedBy"/> when a case closes itself through inactivity.
    /// </summary>
    /// <remarks>
    /// <b>This is what <see cref="SupportCase.ClosedReason"/> reads</b>, which is why the reason is not
    /// stored separately: the store already records who closed a case, and adding a required parameter to
    /// <see cref="ISupportCaseStore.CloseCaseAsync"/> would break every host that implemented the port for
    /// its own storage. Deriving costs nothing and breaks nobody.
    /// </remarks>
    public const string AutoClose = "system:auto-close";
}
