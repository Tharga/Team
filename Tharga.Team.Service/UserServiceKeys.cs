using System.ComponentModel;

namespace Tharga.Team.Service;

/// <summary>
/// The key the undecorated <see cref="IUserService"/> is registered under, for the one caller that has no
/// signed-in user to authorize.
/// </summary>
/// <remarks>
/// <b>Not for components, controllers or MCP providers.</b> <c>ApiKeyAuthenticationHandler</c> verifies a
/// credential before any caller exists, so it cannot hold <c>users:manage</c>; the gated service would refuse
/// every private key. Anything else that resolves this bypasses authorization, and an architecture test
/// names the types allowed to.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class UserServiceKeys
{
    /// <summary>The service key of the undecorated user service.</summary>
    public const string Inner = "inner-user-service";
}
