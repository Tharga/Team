namespace Tharga.Team.Support.Cases;

/// <summary>
/// Where a mail that opens a case lands: in one team, unassigned with teams suggested, or unassigned.
/// </summary>
/// <param name="Sender">The account the case is opened for, when it opens in <see cref="Team"/>; otherwise null.</param>
/// <param name="Team">The team the case opens in, or null for unassigned.</param>
/// <param name="Suggestions">Teams staff are offered instead; empty when it opens in a team or nothing matched.</param>
/// <param name="Reason">Why there are suggestions rather than a team; null when there are none.</param>
/// <remarks>
/// <b>Only one answer places a case</b>: a verified sender, one account holding the address, one team. Every
/// other match is information for the person who decides, because the cost of guessing is one customer's
/// problem in another customer's list — and an unverified <c>From</c> can name anybody.
/// </remarks>
internal sealed record SupportSenderPlacement(SupportSender Sender, SupportCaseTeam Team, SupportCaseTeam[] Suggestions, SupportTeamSuggestionReason? Reason)
{
    public static SupportSenderPlacement Unassigned { get; } = new(null, null, [], null);

    public static SupportSenderPlacement Decide(bool senderVerified, IReadOnlyList<SupportSender> senders)
    {
        var teams = (senders ?? [])
            .SelectMany(x => x?.Teams ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x?.Key))
            .DistinctBy(x => x.Key)
            .ToArray();

        if (teams.Length == 0) return Unassigned;

        if (!senderVerified) return Suggest(teams, SupportTeamSuggestionReason.SenderNotVerified);
        if (senders.Count > 1) return Suggest(teams, SupportTeamSuggestionReason.SharedAddress);
        if (teams.Length > 1) return Suggest(teams, SupportTeamSuggestionReason.SeveralTeams);

        return new SupportSenderPlacement(senders[0], teams[0], [], null);
    }

    private static SupportSenderPlacement Suggest(SupportCaseTeam[] teams, SupportTeamSuggestionReason reason) => new(null, null, teams, reason);
}
