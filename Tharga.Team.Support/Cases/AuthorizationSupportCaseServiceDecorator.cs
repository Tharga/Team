using Tharga.Team.Service;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// The single enforcement point for support-case operations.
/// </summary>
/// <remarks>
/// <b>Not every check is a scope, and that is deliberate.</b> Raising a case about your own team, listing
/// your own cases, and replying to a case you raised are things an ordinary member does; gating them behind
/// a grant would mean every host granting it to everybody, and a scope everyone holds checks nothing. They
/// are authorized by <i>membership</i> and by <i>authorship</i> instead — still checked, just not by a
/// scope. Reading or acting on <i>somebody else's</i> case is the privileged act, and that is what
/// <see cref="SupportScopes.Read"/> and <see cref="SupportScopes.Manage"/> govern.
/// <para>
/// <b>Why <c>support:read</c> is a real boundary rather than a formality.</b> A support case contains
/// whatever a user typed into it, which is exactly where somebody pastes a password, a token or a
/// customer's details. Reading other people's cases deserves its own grant for the same reason the audit log
/// does.
/// </para>
/// <para>
/// <b>Membership is checked before authorship on every path.</b> Authorship alone would let someone who has
/// left the team keep acting on their old cases; membership alone would let any member act on any case. Both
/// paths through <see cref="RequireCaseAccessAsync"/> require the caller to be in the team first, and the
/// case itself is always loaded through the team so an id from another tenant simply does not resolve.
/// </para>
/// </remarks>
internal sealed class AuthorizationSupportCaseServiceDecorator(ISupportCaseService inner, TeamAuthorizer authorizer) : ISupportCaseService
{
    public async Task<SupportCase> RaiseCaseAsync(string teamKey, string subject, string body, CancellationToken cancellationToken = default)
    {
        await RequireMembershipAsync(teamKey);

        return await inner.RaiseCaseAsync(teamKey, subject, body, cancellationToken);
    }

    public async Task ReplyToCaseAsync(string teamKey, string caseId, string body, CancellationToken cancellationToken = default)
    {
        await RequireCaseAccessAsync(teamKey, caseId, "reply to");

        await inner.ReplyToCaseAsync(teamKey, caseId, body, cancellationToken);
    }

    public async Task CloseCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
    {
        await RequireCaseAccessAsync(teamKey, caseId, "close");

        await inner.CloseCaseAsync(teamKey, caseId, cancellationToken);
    }

    public async Task<SupportCase> GetCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
    {
        await RequireCaseAccessAsync(teamKey, caseId, "read");

        return await inner.GetCaseAsync(teamKey, caseId, cancellationToken);
    }

    public async Task<SupportCasePage> GetCasesAsync(string teamKey, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        await RequireMembershipAsync(teamKey);
        await RequireScopeAsync(SupportScopes.Read, teamKey);

        return await inner.GetCasesAsync(teamKey, cursor, pageSize, cancellationToken);
    }

    public async Task<SupportCasePage> GetMyCasesAsync(string teamKey, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        await RequireMembershipAsync(teamKey);

        return await inner.GetMyCasesAsync(teamKey, cursor, pageSize, cancellationToken);
    }

    public async Task<SupportMessagePage> GetMessagesAsync(string teamKey, string caseId, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        await RequireCaseAccessAsync(teamKey, caseId, "read");

        return await inner.GetMessagesAsync(teamKey, caseId, cursor, pageSize, cancellationToken);
    }

    /// <remarks>
    /// <b>Reuses the case-access check rather than repeating something similar.</b> Marking a case read is a
    /// write on that case, so it must be exactly as hard as reading it — a second check that merely resembles
    /// the first is how the two drift apart until one of them is wrong.
    /// </remarks>
    public async Task MarkReadAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
    {
        await RequireCaseAccessAsync(teamKey, caseId, "mark read");

        await inner.MarkReadAsync(teamKey, caseId, cancellationToken);
    }

    public async Task<int> GetMyUnreadCountAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        await RequireMembershipAsync(teamKey);

        return await inner.GetMyUnreadCountAsync(teamKey, cancellationToken);
    }

    /// <remarks>
    /// Counts across every case in the team, including cases the caller did not raise — so it is exactly as
    /// privileged as reading them, and takes the same scope.
    /// </remarks>
    public async Task<int> GetAwaitingSupportCountAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        await RequireMembershipAsync(teamKey);
        await RequireScopeAsync(SupportScopes.Read, teamKey);

        return await inner.GetAwaitingSupportCountAsync(teamKey, cancellationToken);
    }

    private async Task RequireMembershipAsync(string teamKey)
    {
        if (!await authorizer.IsMemberOfAsync(teamKey))
            throw new UnauthorizedAccessException(
                $"Support cases for team '{teamKey}' require being a member of that team with it selected.");
    }

    private async Task RequireScopeAsync(string scope, string teamKey)
    {
        if (!await authorizer.HasTeamScopeAsync(scope, teamKey))
            throw new UnauthorizedAccessException(
                $"This operation on team '{teamKey}' requires the '{scope}' scope on that team.");
    }

    /// <summary>
    /// The caller may act on this case if they raised it, or if they hold the managing scope for the team.
    /// </summary>
    /// <remarks>
    /// <b>The case is loaded through the team, which is what closes the cross-tenant hole.</b> A caller
    /// presenting a valid case id belonging to another team gets a miss rather than a leak, because the store
    /// keys on the pair — so guessing or replaying an id gains nothing.
    /// <para>
    /// A missing case and an inaccessible one are reported the same way on purpose: telling an unauthorized
    /// caller that a case exists is itself a disclosure.
    /// </para>
    /// </remarks>
    private async Task RequireCaseAccessAsync(string teamKey, string caseId, string verb)
    {
        await RequireMembershipAsync(teamKey);

        if (await authorizer.HasTeamScopeAsync(SupportScopes.Manage, teamKey)) return;
        if (await authorizer.HasTeamScopeAsync(SupportScopes.Read, teamKey)) return;

        var supportCase = await inner.GetCaseAsync(teamKey, caseId);
        var subject = await authorizer.GetSubjectAsync();

        if (supportCase != null && !string.IsNullOrEmpty(subject) && supportCase.AuthorIdentity == subject) return;

        throw new UnauthorizedAccessException(
            $"Only the member who raised support case '{caseId}', or a caller holding " +
            $"'{SupportScopes.Read}' or '{SupportScopes.Manage}' on team '{teamKey}', may {verb} it.");
    }
}
