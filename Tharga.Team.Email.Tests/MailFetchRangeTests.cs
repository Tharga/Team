using MailKit;

namespace Tharga.Team.Email.Tests;

public class MailFetchRangeTests
{
    [Fact]
    public void WhenNothingIsNewer_TheHighestUidTheServerAnswersWithIsNotReadAgain()
    {
        Assert.Empty(MailClient.Newer([new UniqueId(3)], 3));
    }

    [Fact]
    public void OnlyTheUidsAboveThePosition_AreRead_InOrder()
    {
        Assert.Equal([4u, 5u], MailClient.Newer([new UniqueId(5), new UniqueId(3), new UniqueId(4)], 3).Select(x => x.Id));
    }

    [Fact]
    public void FromTheStart_EverythingIsRead()
    {
        Assert.Equal([1u, 2u], MailClient.Newer([new UniqueId(1), new UniqueId(2)], 0).Select(x => x.Id));
    }
}
