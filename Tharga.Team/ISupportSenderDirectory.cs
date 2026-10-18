using System.ComponentModel;

namespace Tharga.Team;

/// <summary>
/// Who a support mail's sender is in the product: the user accounts holding the address, and the teams each
/// is an active member of.
/// </summary>
/// <remarks>
/// <b>Internal, and deliberately unchecked.</b> The mail poller reads it on nobody's behalf to decide whether a
/// new case may open in a team, so there is no caller to check. It answers for every user and every team, and
/// a component, controller or MCP provider injecting it would hand that to whoever is signed in.
/// <para>
/// <b>A match says whose address it is, never who sent the mail.</b> A <c>From</c> header authenticates nobody.
/// The support module places a case in a team only when the mail transport has verified the sender and this
/// finds exactly one account in exactly one team; anything less becomes a suggestion for staff.
/// </para>
/// <para>
/// <b>The address compared is the one on the user record</b>, which comes from the sign-in provider's e-mail
/// claim. A host whose provider issues addresses it has not verified should replace this registration with
/// one that finds nobody.
/// </para>
/// <para>
/// <b>Optional.</b> With none registered every new mail opens unassigned without suggestions, which is how
/// support mail behaved before this existed.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ISupportSenderDirectory
{
    /// <summary>
    /// The user accounts whose address is <paramref name="address"/>, compared trimmed and ignoring case, each
    /// with the teams it is an active member of; empty when no account holds it.
    /// </summary>
    Task<SupportSender[]> FindAsync(string address, CancellationToken cancellationToken = default);
}
