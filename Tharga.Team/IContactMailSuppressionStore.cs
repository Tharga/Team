using System.ComponentModel;

namespace Tharga.Team;

/// <summary>Keeps the addresses that opted out of contact mail, of one category or of all of it, so that none is mailed there again; internal and unchecked, so an address is opted out through the unsubscribe link or the checked contact message service, never by injecting this.</summary>
/// <remarks>
/// <b>Addresses are stored and compared trimmed and in lower case</b>, so an opt-out holds however the address is written
/// in the next list. One opt-out is kept per address and category, the first: asking again changes nothing.
/// <c>Tharga.Team.MongoDB</c> registers one; register another before it to keep them elsewhere.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IContactMailSuppressionStore
{
    /// <summary>Records the opt-out, or keeps the one already recorded for the same address and category; true when it was not recorded before.</summary>
    Task<bool> AddAsync(ContactMailSuppression suppression, CancellationToken cancellationToken = default);

    /// <summary>Every opt-out recorded for these addresses, of a category or of all contact mail.</summary>
    Task<IReadOnlyList<ContactMailSuppression>> GetAsync(IReadOnlyCollection<string> addresses, CancellationToken cancellationToken = default);
}
