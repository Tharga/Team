namespace Tharga.Team.Service.Audit;

/// <summary>
/// The kinds of thing an audit entry can say an operation was done to — the values of <see cref="AuditEntry.TargetKind"/>.
/// </summary>
/// <remarks>
/// Plain strings, not an enum, so a host that audits something of its own can name its own kind without a change here.
/// A host's own entry carries a target through <c>entry with { TargetKind = ..., TargetKey = ... }</c>.
/// </remarks>
public static class AuditTargetKinds
{
    /// <summary>A user, by <see cref="IUser.Key"/>.</summary>
    public const string User = "user";

    /// <summary>A team, by <see cref="ITeam.Key"/>.</summary>
    public const string Team = "team";

    /// <summary>An API key, by the key's id (<see cref="IApiKey.Key"/>).</summary>
    public const string ApiKey = "apikey";
}
