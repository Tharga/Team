using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Purging a team removes the files its cases carried, before the cases that name them are gone.
/// </summary>
public class SupportCasePurgeAttachmentTests
{
    [Fact]
    public async Task PurgingATeam_RemovesEveryFileOfItsCases_AndNoOtherTeams()
    {
        var cases = new InMemorySupportCaseStore();
        var files = new InMemorySupportAttachmentStore();

        await cases.AddCaseAsync(Case("a-1", "team-a"), First());
        await cases.AddCaseAsync(Case("a-2", "team-a"), First());
        await cases.AddCaseAsync(Case("b-1", "team-b"), First());
        await files.SaveAsync("a-1", Upload());
        await files.SaveAsync("a-2", Upload());
        await files.SaveAsync("b-1", Upload());

        var purged = await new SupportCasePurgeParticipant(cases, files).PurgeTeamDataAsync("team-a");

        Assert.Equal(2, purged);
        Assert.Equal(["b-1"], files.Files.Select(x => x.CaseId).ToArray());
    }

    [Fact]
    public async Task WithNoStore_TheCasesAreStillPurged()
    {
        var cases = new InMemorySupportCaseStore();
        await cases.AddCaseAsync(Case("a-1", "team-a"), First());

        Assert.Equal(1, await new SupportCasePurgeParticipant(cases).PurgeTeamDataAsync("team-a"));
    }

    private static SupportAttachmentUpload Upload() => new() { FileName = "f.pdf", ContentType = "application/pdf", Content = new MemoryStream([1]) };

    private static SupportCase Case(string id, string teamKey) => new()
    {
        Id = id,
        TeamKey = teamKey,
        AuthorIdentity = "sub-1",
        AuthorName = "A User",
        Subject = "Subject",
        Status = SupportCaseStatus.Open,
        CreatedAt = DateTime.UtcNow,
        MessageCount = 1
    };

    private static SupportMessage First() => new()
    {
        Sequence = 1,
        Kind = SupportMessageKind.User,
        Body = "Body",
        SentAt = DateTime.UtcNow
    };
}
