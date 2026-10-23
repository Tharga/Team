namespace Tharga.Team.Email.Tests.Mailbox;

/// <summary>
/// Which changes a host relaying to several instances must send on to the staff who read the postmaster: those with no
/// team, and a routing, which names the team the mail went to and takes it off the postmaster's list.
/// </summary>
public class TeamMailUpdatedEventArgsTests
{
    [Theory]
    [InlineData(null, TeamMailChange.Received, true)]
    [InlineData(null, TeamMailChange.Archived, true)]
    [InlineData(null, TeamMailChange.Restored, true)]
    [InlineData(null, TeamMailChange.Handled, true)]
    [InlineData("team-a", TeamMailChange.Rerouted, true)]
    [InlineData("team-a", TeamMailChange.Received, false)]
    [InlineData("team-a", TeamMailChange.Handled, false)]
    [InlineData("team-a", TeamMailChange.Archived, false)]
    [InlineData("team-a", TeamMailChange.Restored, false)]
    public void AChange_AffectsThePostmaster_WhenItHasNoTeamOrIsARouting(string teamKey, TeamMailChange change, bool expected)
    {
        var args = new TeamMailUpdatedEventArgs { TeamKey = teamKey, MailId = "mail-1", Change = change };

        Assert.Equal(expected, args.AffectsPostmaster);
    }
}
