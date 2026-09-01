namespace Tharga.Team.Support.Tests;

/// <summary>
/// Why a case is closed, read from who closed it.
/// </summary>
/// <remarks>
/// <b>Derived rather than stored</b>, so that closing needed no change to <c>ISupportCaseStore</c> — a port
/// hosts implement for their own storage, where a new required parameter is a compile-time break in somebody
/// else's repository. These pin the derivation, which is the part that would otherwise drift.
/// </remarks>
public class ClosureReasonTests
{
    private static SupportCase Case(SupportCaseStatus status, string closedBy = null) => new()
    {
        Id = "case-1",
        TeamKey = "acme",
        AuthorIdentity = "alice",
        AuthorName = "Alice",
        Subject = "Export is empty",
        Status = status,
        CreatedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
        ClosedBy = closedBy,
        MessageCount = 1
    };

    [Fact]
    public void AnOpenCase_HasNoClosureReason()
    {
        Assert.Null(Case(SupportCaseStatus.Open).ClosedReason);
    }

    /// <summary>
    /// An open case that carries a stale <c>ClosedBy</c> — from a reopen that cleared the status but not the
    /// actor — must still read as open rather than as closed by whoever closed it last time.
    /// </summary>
    [Fact]
    public void AnOpenCase_HasNoReason_EvenWithAnActorLeftOnIt()
    {
        Assert.Null(Case(SupportCaseStatus.Open, SupportCaseActors.AutoClose).ClosedReason);
    }

    [Fact]
    public void AClosedCase_IsManualWhenAPersonClosedIt()
    {
        Assert.Equal(SupportCaseClosureReason.Manual, Case(SupportCaseStatus.Closed, "bob").ClosedReason);
    }

    [Fact]
    public void AClosedCase_IsInactivityWhenTheSweeperClosedIt()
    {
        Assert.Equal(
            SupportCaseClosureReason.Inactivity,
            Case(SupportCaseStatus.Closed, SupportCaseActors.AutoClose).ClosedReason);
    }

    /// <summary>
    /// A closed case with no recorded actor is somebody's doing that was not captured — closer to manual than
    /// to automatic, and never to be shown as "closed itself".
    /// </summary>
    [Fact]
    public void AClosedCaseWithNoActor_IsManual()
    {
        Assert.Equal(SupportCaseClosureReason.Manual, Case(SupportCaseStatus.Closed).ClosedReason);
    }

    /// <summary>
    /// The prefix is what stops a real authentication subject reading as the toolkit.
    /// </summary>
    [Fact]
    public void TheAutoCloseActor_CannotBeMistakenForAUser()
    {
        Assert.StartsWith("system:", SupportCaseActors.AutoClose);
    }
}
