namespace Tharga.Team.Email.Users;

/// <summary>The one form a recipient's address is stored and compared in, so a mail queued to an address finds its way into the history of the user who later holds it.</summary>
internal static class UserMailAddresses
{
    /// <summary>The address trimmed and in lower case, or null when there is none.</summary>
    public static string Normalize(string address)
        => string.IsNullOrWhiteSpace(address) ? null : address.Trim().ToLowerInvariant();
}
