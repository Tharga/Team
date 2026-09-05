using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Reading a transcript, and what bounds one.
/// </summary>
public class SupportCaseTranscriptTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";

    /// <summary>
    /// The reason the cursor is a message sequence rather than an offset.
    /// </summary>
    /// <remarks>
    /// <b>A support conversation is appended to while somebody is reading it.</b> With an offset cursor, a
    /// reply arriving between two page reads shifts every later entry, so the reader silently sees one
    /// twice or misses one entirely. Keying on the sequence of the last item returned cannot do that, and
    /// this test is what would catch a change back to skip/take.
    /// </remarks>
    [Fact]
    public async Task Paging_DoesNotSkipOrDuplicate_WhenTheCaseIsAppendedToMidRead()
    {
        var service = Build();
        var raised = await service.RaiseCaseAsync(TeamA, "Subject", "Message 1");

        for (var i = 2; i <= 5; i++)
            await service.ReplyToCaseAsync(TeamA, raised.Id, $"Message {i}");

        var first = await service.GetMessagesAsync(TeamA, raised.Id, pageSize: 2);
        Assert.Equal(["Message 1", "Message 2"], first.Items.Select(x => x.Body));

        // A reply lands between the two page reads.
        await service.ReplyToCaseAsync(TeamA, raised.Id, "Message 6");

        var second = await service.GetMessagesAsync(TeamA, raised.Id, first.NextCursor, pageSize: 2);

        Assert.Equal(["Message 3", "Message 4"], second.Items.Select(x => x.Body));
    }

    [Fact]
    public async Task ATranscript_IsReturnedOldestFirst()
    {
        var service = Build();
        var raised = await service.RaiseCaseAsync(TeamA, "Subject", "First");
        await service.ReplyToCaseAsync(TeamA, raised.Id, "Second");

        var messages = await service.GetMessagesAsync(TeamA, raised.Id);

        Assert.Equal(["First", "Second"], messages.Items.Select(x => x.Body));
        Assert.Equal([1, 2], messages.Items.Select(x => x.Sequence));
    }

    /// <summary>
    /// Provenance is null for anything written through the application, which is what makes a non-null value
    /// mean something: it says the entry came in through a door where the author was not authenticated.
    /// </summary>
    [Fact]
    public async Task AMessageWrittenThroughTheService_RecordsNoChannelSource()
    {
        var service = Build();
        var raised = await service.RaiseCaseAsync(TeamA, "Subject", "First");
        await service.ReplyToCaseAsync(TeamA, raised.Id, "Second");

        var messages = await service.GetMessagesAsync(TeamA, raised.Id);

        Assert.All(messages.Items, x => Assert.Null(x.Source));
    }

    [Fact]
    public async Task AMessageLongerThanTheLimit_IsRefused()
    {
        var service = Build();
        var tooLong = new string('x', SupportCaseLimits.MaxMessageLength + 1);

        var raising = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RaiseCaseAsync(TeamA, "Subject", tooLong));
        Assert.Contains($"{SupportCaseLimits.MaxMessageLength}", raising.Message);

        var raised = await service.RaiseCaseAsync(TeamA, "Subject", "Body");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReplyToCaseAsync(TeamA, raised.Id, tooLong));
    }

    [Fact]
    public async Task AMessageAtExactlyTheLimit_IsAccepted()
    {
        var service = Build();

        var atLimit = new string('x', SupportCaseLimits.MaxMessageLength);

        var raised = await service.RaiseCaseAsync(TeamA, "Subject", atLimit);

        Assert.Equal(1, raised.MessageCount);
    }

    /// <summary>
    /// The transcript is embedded in one document, so it has to stop growing somewhere. The remedy is a new
    /// case, and the error says so.
    /// </summary>
    [Fact]
    public async Task AFullCase_RefusesFurtherReplies_AndSaysWhatToDoInstead()
    {
        var store = new InMemorySupportCaseStore();
        var service = Build(store);

        var raised = await service.RaiseCaseAsync(TeamA, "Subject", "Message 1");

        store.Stuff(TeamA, raised.Id, SupportCaseLimits.MaxMessagesPerCase);

        var full = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReplyToCaseAsync(TeamA, raised.Id, "One more"));

        Assert.Contains($"{SupportCaseLimits.MaxMessagesPerCase}", full.Message);
        Assert.Contains("Raise a new case", full.Message);
    }

    private static ISupportCaseService Build(InMemorySupportCaseStore store = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Alice),
            new(ClaimTypes.Name, Alice),
            new(TeamClaimTypes.TeamKey, TeamA)
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

        return new AuthorizationSupportCaseServiceDecorator(
            new SupportCaseService(store ?? new InMemorySupportCaseStore(), authorizer, TimeProvider.System),
            authorizer);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
