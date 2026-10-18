namespace Tharga.Team.Support.Cases;

/// <summary>
/// Destroys a team's support cases when the team is purged.
/// </summary>
/// <remarks>
/// The store already exposed <c>DeleteCasesForTeamAsync</c> and nothing called it — the support-cases
/// release shipped with that gap documented, because there was no way to reach the store from the purge
/// site. This participant is the wiring that was missing.
/// <para>
/// A case holds whatever a user typed into it, so leaving a purged tenant's cases behind is not only untidy:
/// it is retaining that tenant's free-form content after they have been removed.
/// </para>
/// <para>
/// <b>The files go first</b>, case by case while the cases can still be listed; the attachment store is keyed by
/// case, so once the cases are gone nothing names what to remove.
/// </para>
/// </remarks>
internal sealed class SupportCasePurgeParticipant(ISupportCaseStore store, ISupportAttachmentStore attachmentStore = null) : ITeamPurgeParticipant
{
    private const int PageSize = 100;

    public string Name => "support cases";

    public async Task<int> PurgeTeamDataAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        if (attachmentStore != null) await DeleteAttachmentsAsync(teamKey, cancellationToken);

        return await store.DeleteCasesForTeamAsync(teamKey, cancellationToken);
    }

    private async Task DeleteAttachmentsAsync(string teamKey, CancellationToken cancellationToken)
    {
        string cursor = null;

        do
        {
            var page = await store.GetCasesAsync(teamKey, cursor, PageSize, cancellationToken);

            foreach (var supportCase in page?.Items ?? [])
                await attachmentStore.DeleteForCaseAsync(supportCase.Id, cancellationToken);

            cursor = page?.NextCursor;
        } while (cursor != null);
    }
}
