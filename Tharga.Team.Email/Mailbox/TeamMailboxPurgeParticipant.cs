namespace Tharga.Team.Email.Mailbox;

/// <summary>
/// Removes a team's mailbox with the team: its keys, its mail, and the content the host keeps for that mail.
/// </summary>
/// <remarks>
/// The keys go too, so the team's addresses become unknown rather than retired; mail still sent to them after
/// the team is gone reaches the postmaster, never a team that later draws the same key.
/// </remarks>
internal sealed class TeamMailboxPurgeParticipant(ITeamMailKeyStore keys, ITeamMailStore mail, IInboundMailContentStore content = null) : ITeamPurgeParticipant
{
    public string Name => "team mailbox";

    public async Task<int> PurgeTeamDataAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var removed = await mail.DeleteForTeamAsync(teamKey, cancellationToken);

        if (content != null)
        {
            foreach (var mailId in removed) await content.DeleteAsync(mailId, cancellationToken);
        }

        await keys.DeleteForTeamAsync(teamKey, cancellationToken);

        return removed.Length;
    }
}
