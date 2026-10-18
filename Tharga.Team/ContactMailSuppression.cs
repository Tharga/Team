namespace Tharga.Team;

/// <summary>An address that asked not to receive contact mail: of one category, or of any contact mail from the product.</summary>
/// <remarks>
/// <b>Kept for good.</b> It is the record that the person asked not to be mailed, and the only thing that keeps a later
/// mailing from reaching them, so it does not expire with the mail it was made from.
/// </remarks>
public sealed record ContactMailSuppression
{
    /// <summary>The address, trimmed and in lower case.</summary>
    public required string Address { get; init; }

    /// <summary>The contact category it stops, or null when it stops every contact mail to the address.</summary>
    public string Category { get; init; }

    /// <summary>When it was asked for, in UTC.</summary>
    public required DateTime SuppressedAt { get; init; }

    /// <summary>How it was asked for.</summary>
    public required ContactMailSuppressionSource Source { get; init; }

    /// <summary>Whether it stops mail in the category with this key.</summary>
    public bool AppliesTo(string categoryKey) => Category == null || string.Equals(Category, categoryKey, StringComparison.Ordinal);
}

/// <summary>How an address came to be opted out of contact mail.</summary>
public enum ContactMailSuppressionSource
{
    /// <summary>The person followed the unsubscribe link in a mail and confirmed on its page.</summary>
    Link,

    /// <summary>The person's mail provider posted the one-click unsubscribe request (RFC 8058) on their behalf.</summary>
    OneClick,

    /// <summary>Staff of the product opted the address out, as asked by the person some other way.</summary>
    Staff
}
