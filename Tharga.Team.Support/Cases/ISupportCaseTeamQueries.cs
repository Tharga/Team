namespace Tharga.Team.Support.Cases;

/// <summary>
/// The team reads narrowed to what a team reader may see under per-case visibility.
/// </summary>
/// <remarks>
/// Internal and unchecked: <see cref="AuthorizationSupportCaseServiceDecorator"/> decides when a caller gets
/// these rather than the full team reads, so the service still authorizes nothing.
/// </remarks>
internal interface ISupportCaseTeamQueries
{
    Task<SupportCasePage> GetCasesVisibleToTeamAsync(string teamKey, string cursor, int pageSize, CancellationToken cancellationToken);

    Task<int> GetAwaitingSupportCountVisibleToTeamAsync(string teamKey, CancellationToken cancellationToken);
}
