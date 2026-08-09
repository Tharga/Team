using System.Security.Claims;

namespace Tharga.Team;

/// <summary>The user record created on someone's first sign-in, and the principal it was created from.</summary>
/// <remarks>
/// A class rather than a record because it derives from <see cref="EventArgs"/>, matching
/// <see cref="SelectedTeamChangedEventArgs"/>.
/// </remarks>
public class UserCreatedEventArgs : EventArgs
{
    public UserCreatedEventArgs(IUser user, ClaimsPrincipal principal)
    {
        User = user;
        Principal = principal;
    }

    /// <summary>The newly stored user.</summary>
    public IUser User { get; }

    /// <summary>The principal that triggered the creation — the actor an audit entry needs.</summary>
    public ClaimsPrincipal Principal { get; }
}
