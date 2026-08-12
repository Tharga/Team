namespace Tharga.Team;

/// <summary>
/// A team's underlying storage could not be removed. Carries the team key and the store's own failure as
/// <see cref="System.Exception.InnerException"/>.
/// </summary>
/// <remarks>
/// <b>Exists so a deployment problem stops arriving as a driver stack trace.</b> Purging a team in a
/// per-team-database deployment drops that database, and dropping a database is a privilege most managed
/// deployments do not give an application's data-plane user — in MongoDB Atlas, `readWriteAnyDatabase`
/// does <i>not</i> include `dropDatabase`. The refusal therefore comes from the store, one layer below
/// authorization, after every scope check has already passed.
/// <para>
/// The operator can do nothing about it from the UI, so the message names what the <i>deployment</i> has to
/// grant rather than suggesting the caller lacks permission. Reported by Eplicta FortDocs
/// (Tharga/Team#224), where a `MongoCommandException` reached the error page.
/// </para>
/// <para>
/// Thrown by the storage adapter, not by the domain. A missing <i>scope</i> is still
/// <see cref="System.UnauthorizedAccessException"/> — the two are different failures with different fixes,
/// and collapsing them would send an operator to the wrong place.
/// </para>
/// </remarks>
public sealed class TeamStorageException : Exception
{
    /// <param name="teamKey">The team whose storage could not be removed.</param>
    /// <param name="message">What failed, and what a deployment must grant to make it work.</param>
    /// <param name="innerException">The store's own exception, preserved for diagnostics.</param>
    public TeamStorageException(string teamKey, string message, Exception innerException)
        : base(message, innerException)
    {
        TeamKey = teamKey;
    }

    /// <summary>The team whose storage could not be removed.</summary>
    public string TeamKey { get; }
}
