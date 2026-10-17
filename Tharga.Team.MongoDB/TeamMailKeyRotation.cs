namespace Tharga.Team.MongoDB;

/// <summary>
/// What a rotation does to a team's keys: a still-accepted secondary retires now, the primary becomes the
/// secondary until it retires, and the new key becomes the primary. Retired keys are kept.
/// </summary>
internal static class TeamMailKeyRotation
{
    public static TeamMailKeyEntry[] Rotate(IEnumerable<TeamMailKeyEntry> keys, TeamMailKeyEntry newPrimary, DateTime retiresAt, DateTime now) =>
    [
        .. keys.Select(x => x.Role switch
        {
            MailKeyRole.Primary => x with { Role = MailKeyRole.Secondary, RetiresAt = retiresAt },
            MailKeyRole.Secondary when x.RetiresAt is not { } retires || retires > now => x with { RetiresAt = now },
            _ => x
        }),
        newPrimary
    ];
}
