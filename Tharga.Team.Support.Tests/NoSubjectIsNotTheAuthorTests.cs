using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// A caller with no subject is never taken for the author of a case with no author (Tharga/Team#547).
/// </summary>
/// <remarks>
/// <b>Two empty identities must not match.</b> A shared API key carries no subject, and a case raised without one, by mail from an unknown sender for
/// instance, carries no author. The caller got past the case-access check through a scope, and the author-only acts compared the two: null equals null, so
/// the caller could run the assistant on the case or rate its answers. Only the person who raised a case may do either.
/// </remarks>
public class NoSubjectIsNotTheAuthorTests
{
    private const string Team = "team-a";
    private const string CaseId = "case-1";

    private readonly ISupportCaseService _inner = Substitute.For<ISupportCaseService>();

    private static SupportCase AuthorlessCase() => new()
    {
        Id = CaseId,
        TeamKey = Team,
        AuthorIdentity = null,
        AuthorName = "unknown@example.com",
        Subject = "Mail from nobody we know",
        Status = SupportCaseStatus.Open,
        Visibility = SupportCaseVisibility.Team,
        CreatedAt = new DateTime(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc),
        MessageCount = 1
    };

    /// <summary>A caller with a team scope and no subject: a shared key, which is a member of nobody.</summary>
    private AuthorizationSupportCaseServiceDecorator Subjectless()
    {
        _inner.GetCaseAsync(Team, CaseId, Arg.Any<CancellationToken>()).Returns(AuthorlessCase());

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "a shared key"),
            new Claim(TeamClaimTypes.ApiKeyId, "key-1"),
            new Claim(TeamClaimTypes.TeamKey, Team),
            new Claim(TeamClaimTypes.Scope, SupportScopes.Manage)
        ], "test"));

        return new AuthorizationSupportCaseServiceDecorator(_inner, new TeamAuthorizer(new FixedPrincipalAccessor(principal)));
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }

    [Fact]
    public async Task TheAssistant_IsNotRun_ForACallerWithNoSubject_OnACaseWithNoAuthor()
    {
        var ran = await Subjectless().RunAssistantAsync(Team, CaseId);

        Assert.False(ran);
        await _inner.DidNotReceiveWithAnyArgs().RunAssistantAsync(default, default, default(CancellationToken));
    }

    [Fact]
    public async Task TheAssistant_IsNotRunWithProgress_ForACallerWithNoSubject_OnACaseWithNoAuthor()
    {
        var ran = await Subjectless().RunAssistantAsync(Team, CaseId, Substitute.For<IProgress<SupportAssistantProgress>>());

        Assert.False(ran);
        await _inner.DidNotReceiveWithAnyArgs().RunAssistantAsync(default, default, default(IProgress<SupportAssistantProgress>), default);
    }

    [Fact]
    public async Task AnAnswer_CannotBeRated_ByACallerWithNoSubject_OnACaseWithNoAuthor()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Subjectless().RateAnswerAsync(Team, CaseId, 1, SupportAnswerRating.Up));

        await _inner.DidNotReceiveWithAnyArgs().RateAnswerAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task TheConversation_CannotBeRated_ByACallerWithNoSubject_OnACaseWithNoAuthor()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Subjectless().RateConversationAsync(Team, CaseId, SupportAnswerRating.Up));

        await _inner.DidNotReceiveWithAnyArgs().RateConversationAsync(default, default, default, default, default);
    }
}
