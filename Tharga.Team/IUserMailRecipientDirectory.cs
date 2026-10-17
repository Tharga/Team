using System.ComponentModel;

namespace Tharga.Team;

/// <summary>Reads the address a user mail goes to from the user record when it is sent; internal and unchecked, because the sender acts on nobody's behalf.</summary>
/// <remarks><c>Tharga.Team.MongoDB</c> registers one over the user repository; a host with its own user store registers its own.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IUserMailRecipientDirectory
{
    /// <summary>The user with this key and the address on their record, or null when there is no such user.</summary>
    Task<UserMailRecipient> FindAsync(string userKey, CancellationToken cancellationToken = default);
}

/// <summary>Who a user mail goes to, as the user record says when it is sent.</summary>
public sealed record UserMailRecipient
{
    /// <summary>The user's key.</summary>
    public required string Key { get; init; }

    /// <summary>The address on the user record; null or empty when it has none.</summary>
    public string EMail { get; init; }

    /// <summary>The user's display name, or null.</summary>
    public string Name { get; init; }
}
