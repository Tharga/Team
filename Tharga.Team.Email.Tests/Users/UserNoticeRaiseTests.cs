namespace Tharga.Team.Email.Tests.Users;

/// <summary>Raising checks the occurrence while the caller can still hear about it, stores it and returns, and leaves everything about mail to the background.</summary>
public class UserNoticeRaiseTests
{
    private readonly UserNoticeFixture _fixture = new();

    private Task<string> RaiseAsync(UserNoticeOccurrence occurrence) => _fixture.Raiser.RaiseAsync(occurrence);

    private static UserNoticeOccurrence Occurrence() => new()
    {
        NoticeKey = UserNoticeFixture.Assigned.Key,
        TeamKey = UserNoticeFixture.Alpha,
        SubjectId = " 42 ",
        SubjectText = "Case\n42",
        Discriminator = "assigned-1",
        Values = new Dictionary<string, string> { ["actor"] = "Bo" },
        LinkPath = "/cases/42",
        Recipients = ["cia", " cia ", "", "dan"]
    };

    [Fact]
    public async Task Raising_StoresTheOccurrence_AndQueuesNoMail()
    {
        var id = await RaiseAsync(Occurrence());

        var record = Assert.Single(_fixture.Outbox.Records);
        Assert.Equal(id, record.Id);
        Assert.Equal(UserNoticeStatus.Pending, record.Status);
        Assert.Equal(UserMailFixture.Start, record.RaisedAt);
        Assert.Equal("42", record.Occurrence.SubjectId);
        Assert.Equal("Case 42", record.Occurrence.SubjectText);
        Assert.Equal(["cia", "dan"], record.Occurrence.Recipients);
        Assert.Empty(_fixture.Outbox.Items);
        Assert.Empty(_fixture.Queued);
    }

    [Fact]
    public async Task ANoticeNotInTheCatalogue_IsRefused()
    {
        var e = await Assert.ThrowsAsync<ArgumentException>(() => RaiseAsync(Occurrence() with { NoticeKey = "case.unknown" }));

        Assert.Contains("case.unknown", e.Message);
        Assert.Empty(_fixture.Outbox.Records);
    }

    [Fact]
    public async Task AnOccurrenceWithoutTeamOrSubject_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => RaiseAsync(Occurrence() with { TeamKey = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => RaiseAsync(Occurrence() with { SubjectId = null }));
        await Assert.ThrowsAsync<ArgumentException>(() => RaiseAsync(Occurrence() with { SubjectId = new string('x', 201) }));
    }

    [Fact]
    public async Task AValueTheNoticeDoesNotDeclare_IsRefused()
    {
        var e = await Assert.ThrowsAsync<ArgumentException>(() => RaiseAsync(Occurrence() with { Values = new Dictionary<string, string> { ["actor"] = "Bo", ["mood"] = "glad" } }));

        Assert.Contains("'mood'", e.Message);
    }

    [Fact]
    public async Task AValueTheNoticeDeclares_IsRequired()
    {
        var e = await Assert.ThrowsAsync<ArgumentException>(() => RaiseAsync(Occurrence() with { Values = new Dictionary<string, string>() }));

        Assert.Contains("'actor'", e.Message);
    }

    [Theory]
    [InlineData("https://elsewhere.example.com/cases/1")]
    [InlineData("//elsewhere.example.com/cases/1")]
    [InlineData("javascript:alert(1)")]
    [InlineData("cases/1")]
    [InlineData("/\\elsewhere")]
    public async Task ALinkOffTheSite_IsRefused(string link)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => RaiseAsync(Occurrence() with { LinkPath = link }));
    }

    [Theory]
    [InlineData("/cases/42", "/cases/42")]
    [InlineData(" /cases/42?tab=log ", "/cases/42?tab=log")]
    [InlineData(null, null)]
    public async Task ASitePath_OrNoLink_IsKept(string link, string stored)
    {
        await RaiseAsync(Occurrence() with { LinkPath = link });

        Assert.Equal(stored, Assert.Single(_fixture.Outbox.Records).Occurrence.LinkPath);
    }

    [Fact]
    public void TheOccurrenceKey_TellsSubjectAndDiscriminatorApart()
    {
        var first = new UserNoticeOccurrence { NoticeKey = "n", TeamKey = "t", SubjectId = "a:b", Discriminator = "c" };
        var second = new UserNoticeOccurrence { NoticeKey = "n", TeamKey = "t", SubjectId = "a", Discriminator = "b:c" };

        Assert.NotEqual(Tharga.Team.Email.Users.UserNoticeRaiser.OccurrenceKey(first), Tharga.Team.Email.Users.UserNoticeRaiser.OccurrenceKey(second));
        Assert.Equal(Tharga.Team.Email.Users.UserNoticeRaiser.OccurrenceKey(first), Tharga.Team.Email.Users.UserNoticeRaiser.OccurrenceKey(first with { TeamKey = "other" }));
    }
}
