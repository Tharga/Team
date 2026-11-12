using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Tharga.Team.Email;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// The per-case reply address an answer carries, such as <c>support+tracking-3a3aac94fd15@example.com</c>, and the lookup
/// that reads one back to its case.
/// </summary>
/// <remarks>
/// <b>A short token instead of the case id</b>, because the id is 24 characters long and says nothing to a person reading
/// the address. The token is stored on the case (<see cref="ISupportCaseStore.TrySetTrackingTokenAsync"/>) and handed out
/// the first time an answer by mail needs it, so a case that existed before needs no migration.
/// <para>
/// <b>An address already sent keeps working.</b> One carrying the full case id still resolves to the case, so a customer
/// replying to an old mail is not lost. A store that cannot hold tokens leaves the id in the address, as before.
/// </para>
/// </remarks>
internal static class CaseReplyAddress
{
    private const int TokenLength = 12;
    private const int Attempts = 3;

    /// <summary>
    /// The per-case address for a case, or <c>null</c> when the mailbox does not use one or cannot carry one.
    /// </summary>
    /// <remarks>
    /// <b>Writes the case's token the first time</b>, which is why a preview of an answer can assign one: it must show
    /// the address that will be sent, and a token drawn again for the real send would not match.
    /// </remarks>
    public static async Task<string> ForAsync(ISupportCaseStore store, MailOptions mailOptions, SupportCase supportCase, ILogger logger, CancellationToken cancellationToken, string supportAddress = null)
    {
        if (!mailOptions.PerCaseReplyTo || supportCase == null) return null;

        var tag = string.IsNullOrWhiteSpace(supportCase.TrackingToken)
            ? await AssignAsync(store, mailOptions, supportCase, logger, cancellationToken)
            : supportCase.TrackingToken;

        // Falls back to the id, which an address has always been able to carry, rather than to no address at all.
        return PerCaseAddress.Build(string.IsNullOrWhiteSpace(supportAddress) ? mailOptions.FromAddress : supportAddress, string.IsNullOrWhiteSpace(tag) ? supportCase.Id : tag);
    }

    /// <summary>The case an inbound mail's per-case address names, whichever of the mailbox's own addresses it was sent to: its sending address or a send-as address.</summary>
    public static async Task<SupportCase> FindByRecipientAsync(ISupportCaseStore store, IEnumerable<string> deliveredTo, MailOptions mailOptions, CancellationToken cancellationToken)
    {
        var own = new[] { mailOptions.FromAddress }.Concat((mailOptions.SendAs ?? []).Select(x => x?.Address)).Where(x => !string.IsNullOrWhiteSpace(x));

        foreach (var address in own)
        {
            var found = await FindAsync(store, PerCaseAddress.CaseIdIn(deliveredTo, address), cancellationToken);
            if (found != null) return found;
        }

        return null;
    }

    /// <summary>
    /// The case a per-case address tag names: a tracking token first, and a full case id otherwise.
    /// </summary>
    public static async Task<SupportCase> FindAsync(ISupportCaseStore store, string tag, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        return await store.GetCaseByTrackingTokenAsync(tag, cancellationToken)
               ?? await store.GetCaseByIdAsync(tag, cancellationToken);
    }

    internal static string NewToken(string prefix)
        => (prefix ?? string.Empty) + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(TokenLength / 2));

    /// <remarks>
    /// <b>A token that is taken is replaced, never reused.</b> The store refuses one that belongs to another case, and a
    /// few draws are enough: twelve hex characters are 2^48 possibilities, so a collision is rare and a second one a
    /// fault elsewhere. When none lands the answer goes out with the case id, which works, and the failure is logged.
    /// </remarks>
    private static async Task<string> AssignAsync(ISupportCaseStore store, MailOptions mailOptions, SupportCase supportCase, ILogger logger, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var held = await store.TrySetTrackingTokenAsync(supportCase.TeamKey, supportCase.Id, NewToken(mailOptions.TrackingPrefix), cancellationToken);

            if (!string.IsNullOrWhiteSpace(held)) return held;
        }

        logger?.LogDebug("Support case {CaseId} was not given a tracking token, so its reply address carries the case id.", supportCase.Id);

        return null;
    }
}
