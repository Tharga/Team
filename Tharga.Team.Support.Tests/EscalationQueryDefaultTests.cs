namespace Tharga.Team.Support.Tests;

/// <summary>
/// A store written before the escalation queries existed keeps compiling and simply never escalates.
/// </summary>
public class EscalationQueryDefaultTests
{
    private static readonly DateTime Cutoff = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AwaitingSupport_DefaultsToNothing()
    {
        ISupportCaseStore store = new StoreWithoutReopen(new InMemorySupportCaseStore());

        var due = await store.GetCasesAwaitingSupportSinceAsync(Cutoff, 10, TestContext.Current.CancellationToken);

        Assert.Empty(due);
    }

    [Fact]
    public async Task UnreadAnswer_DefaultsToNothing()
    {
        ISupportCaseStore store = new StoreWithoutReopen(new InMemorySupportCaseStore());

        var due = await store.GetCasesWithUnreadAnswerSinceAsync(Cutoff, 10, TestContext.Current.CancellationToken);

        Assert.Empty(due);
    }
}
