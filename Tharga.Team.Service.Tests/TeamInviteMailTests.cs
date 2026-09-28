namespace Tharga.Team.Service.Tests;

/// <summary>
/// The invitation overload that carries the team key, and the default that keeps older senders working.
/// </summary>
public class TeamInviteMailTests
{
    private static readonly TeamInviteMail Mail = new()
    {
        RecipientEmail = "kund@example.com",
        RecipientName = "Kund",
        InviteLink = "https://example.com/i",
        TeamKey = "team-42",
        TeamName = "Acme"
    };

    /// <summary>A sender written before the overload existed receives exactly what it always did.</summary>
    [Fact]
    public async Task ASenderWithOnlyTheOldOverload_ReceivesTheSameFourValues()
    {
        var sender = new OldSender();

        await ((ITeamEmailSender)sender).SendInviteAsync(Mail);

        Assert.Equal(("kund@example.com", "Kund", "https://example.com/i", "Acme"), sender.Received);
    }

    [Fact]
    public async Task ASenderImplementingTheNewOverload_ReceivesTheTeamKey()
    {
        var sender = new KeyedSender();

        await ((ITeamEmailSender)sender).SendInviteAsync(Mail);

        Assert.Equal("team-42", sender.Received?.TeamKey);
        Assert.False(sender.OldOverloadCalled);
    }

    private sealed class OldSender : ITeamEmailSender
    {
        public (string, string, string, string)? Received { get; private set; }

        public Task SendInviteAsync(string recipientEmail, string recipientName, string inviteLink, string teamName)
        {
            Received = (recipientEmail, recipientName, inviteLink, teamName);
            return Task.CompletedTask;
        }
    }

    private sealed class KeyedSender : ITeamEmailSender
    {
        public TeamInviteMail Received { get; private set; }
        public bool OldOverloadCalled { get; private set; }

        public Task SendInviteAsync(string recipientEmail, string recipientName, string inviteLink, string teamName)
        {
            OldOverloadCalled = true;
            return Task.CompletedTask;
        }

        public Task SendInviteAsync(TeamInviteMail mail)
        {
            Received = mail;
            return Task.CompletedTask;
        }
    }
}
