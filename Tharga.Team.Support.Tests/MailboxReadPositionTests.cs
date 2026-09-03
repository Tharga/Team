using Tharga.Team.Support.Email;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// How far a mailbox has been read, and when that knowledge has to be thrown away.
/// </summary>
/// <remarks>
/// <b>A stored UID is only meaningful within one UID generation.</b> A server that rebuilds a mailbox — a
/// restore, a migration, some providers on a whim — issues a new <c>UIDVALIDITY</c>, and every UID the
/// deployment remembers now points at a different message. Trusting one then is the single thing that could
/// skip mail silently, which is why the position carries the generation and not just the number.
/// <para>
/// <b>Re-reading is cheap and safe; skipping is neither.</b> The event ledger recognises everything already
/// applied, so discarding a position costs one pass over the mailbox and no duplicate cases. That asymmetry
/// is why every ambiguous case here resolves towards discarding.
/// </para>
/// </remarks>
public class MailboxReadPositionTests
{
    [Fact]
    public void AFreshPosition_IsNeverInvalidated()
    {
        // Nothing has been read, so there is nothing that could refer to the wrong message.
        Assert.False(MailFetchPosition.Start.IsInvalidatedBy(7));
        Assert.False(MailFetchPosition.Start.IsInvalidatedBy(0));
    }

    [Fact]
    public void TheSameGeneration_KeepsThePosition()
    {
        Assert.False(new MailFetchPosition(7, 42).IsInvalidatedBy(7));
    }

    [Fact]
    public void ANewGeneration_DiscardsThePosition()
    {
        Assert.True(new MailFetchPosition(7, 42).IsInvalidatedBy(8));
    }

    /// <summary>
    /// A lower generation counts too. It should not happen, and "should not happen" is exactly the case where
    /// a stored UID is least worth trusting.
    /// </summary>
    [Fact]
    public void AGenerationGoingBackwards_AlsoDiscardsThePosition()
    {
        Assert.True(new MailFetchPosition(7, 42).IsInvalidatedBy(6));
    }

    /// <summary>
    /// The contract type in <c>Tharga.Team</c> and the transport's own must agree about where "nothing read
    /// yet" is, because the poller converts between them on every poll.
    /// </summary>
    [Fact]
    public void TheContractAndTransportStartPositions_Agree()
    {
        Assert.Equal(SupportMailPosition.Start.UidValidity, MailFetchPosition.Start.UidValidity);
        Assert.Equal(SupportMailPosition.Start.LastUid, MailFetchPosition.Start.LastUid);
    }
}
