namespace Tharga.Team.Email.Mailbox;

/// <summary>A mail key as it is kept: whose it is, its role, and when a replaced one stops being accepted.</summary>
internal sealed record StoredMailKey(string TeamKey, string Key, MailKeyRole Role, DateTime CreatedAt, DateTime? RetiresAt)
{
    /// <summary>A replaced key stops being accepted at the moment it retires, not a moment after.</summary>
    public bool HasRetired(DateTime now) => RetiresAt is { } retires && now >= retires;
}
