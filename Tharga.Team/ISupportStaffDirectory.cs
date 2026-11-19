namespace Tharga.Team;

/// <summary>
/// Who may work the support desk, for a host that lets a case be assigned to somebody other than the person assigning it.
/// </summary>
/// <remarks>
/// <b>Implemented by the host, because the toolkit cannot tell staff from users.</b> Who holds a system support scope is decided by
/// the host's own claims, which the toolkit never enumerates. Register an implementation to offer the people who may be chosen;
/// without one, a person can only assign a case to themselves.
/// <para>
/// <b>It lists candidates and grants nothing.</b> Being listed gives nobody access to a case; reading and answering are still
/// governed by the system support scopes. Return only people who hold one, since the list is what the desk offers.
/// </para>
/// </remarks>
public interface ISupportStaffDirectory
{
    /// <summary>The people a case may be assigned to, each identified by the authentication subject they sign in with.</summary>
    Task<SupportCaseAssignee[]> GetStaffAsync(CancellationToken cancellationToken = default);
}
