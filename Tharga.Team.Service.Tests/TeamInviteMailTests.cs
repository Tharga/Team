namespace Tharga.Team.Service.Tests;

/// <summary>
/// An invitation carries the team key as well as its name, so a sender can look the team up by key.
/// </summary>
/// <remarks>
/// Before 4.0 this also covered the default member that forwarded to a four-string overload; that overload was
/// removed in 4.0 and <see cref="ITeamEmailSender.SendInviteAsync(TeamInviteMail)"/> is the only member.
/// </remarks>
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

    [Fact]
    public async Task TheSender_ReceivesTheWholeInvitation_IncludingTheTeamKey()
    {
        var sender = new KeyedSender();

        await sender.SendInviteAsync(Mail);

        Assert.Equal(Mail, sender.Received);
    }

    [Fact]
    public void TheInterface_HasOneMember()
    {
        Assert.Equal([nameof(ITeamEmailSender.SendInviteAsync)], typeof(ITeamEmailSender).GetMethods().Select(m => m.Name));
        Assert.Equal([typeof(TeamInviteMail)], typeof(ITeamEmailSender).GetMethods().Single().GetParameters().Select(p => p.ParameterType));
    }

    private sealed class KeyedSender : ITeamEmailSender
    {
        public TeamInviteMail Received { get; private set; }

        public Task SendInviteAsync(TeamInviteMail mail)
        {
            Received = mail;
            return Task.CompletedTask;
        }
    }
}
