using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Features.User;

/// <summary>
/// Reads how users sign in from the directory and puts the methods on their rows: a page of rows in one call, or
/// every row in batches. Separated from the component so the batching, the order and the cancellation are testable
/// without rendering.
/// </summary>
/// <remarks>
/// <b>Batches, not one call for all.</b> A read maps each user key to a directory id with a store read before the
/// directory is asked, and a directory answers a batch in several requests of its own (Entra: one per 15 ids). One
/// call for every user would give no progress to show, nothing to cancel between, and a single failure that loses
/// everything read so far.
/// </remarks>
internal sealed class SignInMethodReader(
    Func<string[], CancellationToken, Task<UserSignInIdentities[]>> identities,
    SignInMethodOptions options,
    SignInFallbackLabels labels)
{
    /// <summary>How many users one directory call asks about when every user is read.</summary>
    public const int BatchSize = 50;

    /// <summary>Reads the given rows in one call and sets their methods; nothing is set if the call fails.</summary>
    public async Task ReadBatchAsync(UserViewModel[] rows, CancellationToken cancellationToken = default)
    {
        var result = await identities([.. rows.Select(x => x.Key)], cancellationToken);
        var byUser = result.ToDictionary(x => x.UserKey, x => x.Identities);

        foreach (var row in rows)
        {
            // Linked, but the directory no longer knows the account: nothing to show, which is "None".
            row.SignInMethods = byUser.TryGetValue(row.Key, out var found)
                ? SignInMethodResolver.Resolve(found, options, labels)
                : [];
        }
    }

    /// <summary>
    /// Reads the given rows in batches of <see cref="BatchSize"/>, telling <paramref name="batchRead"/> after each
    /// so progress can be shown. A failure or a cancellation stops the reading; the batches already read stay read.
    /// </summary>
    public async Task ReadAsync(IReadOnlyList<UserViewModel> rows, Func<UserViewModel[], Task> batchRead, CancellationToken cancellationToken = default)
    {
        foreach (var batch in rows.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await ReadBatchAsync(batch, cancellationToken);
            await batchRead(batch);
        }
    }
}
