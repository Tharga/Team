using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// A support case raised with a key that has an owner is the owner's: it has an author, the assistant answers it, and its answers can be rated
/// (Tharga/Team#547). The principals here are shaped as <c>ApiKeyAuthenticationHandler</c> builds them for a private key.
/// </summary>
/// <remarks>
/// <b>A shared key stays outside all of it.</b> It carries no subject, so the case it raises has no author and nothing author-only is open to it.
/// </remarks>
public class OwnedKeySupportTests
{
    private const string Team = "team-a";
    private const string Owner = "owner-subject";

    private readonly InMemorySupportCaseStore _store = new();

    /// <summary>A key principal: the key's name and id, its team, and, for an owned key, its owner's subject.</summary>
    private ISupportCaseService AsKey(string ownerSubject)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.Name, "Evaluation key"),
            new(TeamClaimTypes.ApiKeyId, "key-1"),
            new(TeamClaimTypes.TeamKey, Team)
        ];
        if (ownerSubject != null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, ownerSubject));
            claims.Add(new Claim(TeamClaimTypes.ApiKeyOwner, "user-1"));
        }

        return Service(claims);
    }

    private ISupportCaseService Service(List<Claim> claims)
    {
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));

        // Private conversations: the case belongs to its author, as for a host that keeps assistant conversations private.
        return new AuthorizationSupportCaseServiceDecorator(
            new SupportCaseService(_store, authorizer, TimeProvider.System, null, null, new AnsweringResponder()),
            authorizer,
            SupportCaseVisibilityMode.PerCase);
    }

    private ISupportCaseService TheOwnerSignedIn() => Service(
        [new Claim(ClaimTypes.NameIdentifier, Owner), new Claim(ClaimTypes.Name, "Owner"), new Claim(TeamClaimTypes.TeamKey, Team)]);

    private sealed class AnsweringResponder : ISupportResponder
    {
        public Task<SupportAnswer> AnswerAsync(SupportCase supportCase, IReadOnlyList<SupportMessage> transcript, CancellationToken cancellationToken = default)
            => Task.FromResult(SupportAnswer.FromBody("Reindex the export."));
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }

    [Fact]
    public async Task ACaseRaisedWithAnOwnedKey_HasTheOwnerAsItsAuthor()
    {
        var raised = await AsKey(Owner).RaiseCaseAsync(Team, "Export is empty", "Nothing comes out.");

        Assert.Equal(Owner, raised.AuthorIdentity);
    }

    [Fact]
    public async Task TheAssistant_AnswersAPrivateCase_RaisedWithAnOwnedKey()
    {
        var key = AsKey(Owner);
        var raised = await key.RaiseCaseAsync(Team, null, "The export is empty.", SupportAssistance.Assistant);

        var ran = await key.RunAssistantAsync(Team, raised.Id);

        Assert.True(ran);
        var messages = (await key.GetMessagesAsync(Team, raised.Id)).Items;
        Assert.Contains(messages, x => x.Kind == SupportMessageKind.Assistant);
    }

    [Fact]
    public async Task TheAnswerAndTheConversation_CanBeRated_WithTheOwnedKey()
    {
        var key = AsKey(Owner);
        var raised = await key.RaiseCaseAsync(Team, null, "The export is empty.", SupportAssistance.Assistant);
        await key.RunAssistantAsync(Team, raised.Id);

        await key.RateAnswerAsync(Team, raised.Id, 2, SupportAnswerRating.Down);
        await key.RateConversationAsync(Team, raised.Id, SupportAnswerRating.Up, "Mostly fine");

        var read = await key.GetCaseAsync(Team, raised.Id);
        Assert.Equal(SupportAnswerRating.Down, (await key.GetMessagesAsync(Team, raised.Id)).Items.Single(x => x.Sequence == 2).Rating);
        Assert.NotNull(read);
    }

    [Fact]
    public async Task TheCase_IsInTheOwnersHistory_WhoSeesItAsTheirs()
    {
        var raised = await AsKey(Owner).RaiseCaseAsync(Team, "Export is empty", "Nothing comes out.");

        var mine = await TheOwnerSignedIn().GetMyCasesAsync(Team);

        Assert.Contains(mine.Items, x => x.Id == raised.Id);
        Assert.Equal(raised.Id, (await TheOwnerSignedIn().GetCaseAsync(Team, raised.Id)).Id);
    }

    [Fact]
    public async Task TheOwnersKey_SeesTheOwnersCases()
    {
        var raised = await TheOwnerSignedIn().RaiseCaseAsync(Team, "Export is empty", "Nothing comes out.");

        var mine = await AsKey(Owner).GetMyCasesAsync(Team);

        Assert.Contains(mine.Items, x => x.Id == raised.Id);
    }

    // --- a shared key stays outside ---

    [Fact]
    public async Task ACaseRaisedWithASharedKey_HasNoAuthor_AndNothingAuthorOnlyIsOpenToIt()
    {
        var shared = AsKey(null);
        var raised = await shared.RaiseCaseAsync(Team, null, "The export is empty.", SupportAssistance.Assistant);

        Assert.Null(raised.AuthorIdentity);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => shared.RunAssistantAsync(Team, raised.Id));
    }

    /// <summary>Another person's owned key is that person's, not the case author's.</summary>
    [Fact]
    public async Task SomebodyElsesOwnedKey_CannotRunTheAssistantOnTheOwnersCase()
    {
        var raised = await TheOwnerSignedIn().RaiseCaseAsync(Team, null, "The export is empty.", SupportAssistance.Assistant);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => AsKey("another-owner").RunAssistantAsync(Team, raised.Id));
    }
}
