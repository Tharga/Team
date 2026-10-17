using MailKit.Search;

namespace Tharga.Team.Email.Tests;

/// <summary>A mailbox connected with mail already in it starts at a date, rather than turning its whole history into work.</summary>
public class MailClientFirstReadTests
{
    private static readonly DateTime Since = new(2026, 10, 17, 14, 30, 0);

    [Fact]
    public void WithNoStartDate_TheFirstReadTakesEverything()
    {
        var query = Assert.IsType<UidSearchQuery>(MailClient.SearchFor(0, null));

        Assert.Equal(1u, query.Uids[0].Id);
    }

    [Fact]
    public void WithAStartDate_TheFirstReadTakesOnlyMailDeliveredOnOrAfterThatDay()
    {
        var query = Assert.IsType<BinarySearchQuery>(MailClient.SearchFor(0, Since));

        Assert.Equal(SearchTerm.And, query.Term);
        Assert.IsType<UidSearchQuery>(query.Left);
        var date = Assert.IsType<DateSearchQuery>(query.Right);
        Assert.Equal(SearchTerm.DeliveredAfter, date.Term);
        Assert.Equal(Since.Date, date.Date);
    }

    [Fact]
    public void OnceThereIsAPosition_TheStartDateNoLongerApplies()
    {
        var query = Assert.IsType<UidSearchQuery>(MailClient.SearchFor(42, Since));

        Assert.Equal(43u, query.Uids[0].Id);
    }
}
