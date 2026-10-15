using System.Security.Claims;

namespace Tharga.Team;

/// <summary>A stored user's name and/or email, refreshed from a sign-in the host trusts.</summary>
/// <remarks>
/// A class rather than a record because it derives from <see cref="EventArgs"/>, matching
/// <see cref="UserCreatedEventArgs"/>.
/// </remarks>
public class UserProfileRefreshedEventArgs : EventArgs
{
    public UserProfileRefreshedEventArgs(IUser user, string name, string email, ClaimsPrincipal principal)
    {
        User = user;
        Name = name;
        EMail = email;
        Principal = principal;
    }

    /// <summary>The user as stored before the refresh.</summary>
    public IUser User { get; }

    /// <summary>The name written, or null when the name was left unchanged.</summary>
    public string Name { get; }

    /// <summary>The email written, or null when the email was left unchanged.</summary>
    public string EMail { get; }

    /// <summary>The principal whose claims the values came from — the actor an audit entry needs.</summary>
    public ClaimsPrincipal Principal { get; }
}
