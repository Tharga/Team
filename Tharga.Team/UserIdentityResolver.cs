using System.Security.Claims;
using Tharga.Toolkit;

namespace Tharga.Team;

/// <summary>
/// The one place a principal is turned into the identity a user is stored and audited under.
/// </summary>
/// <remarks>
/// <b>Unconfigured, every caller keeps exactly the behaviour it had before this type existed.</b> The user
/// record is found by Tharga.Toolkit's <c>GetIdentity()</c> chain, and the audit subject is
/// <see cref="ClaimTypes.NameIdentifier"/> alone.
/// <para>
/// <b>Configured, both answers come from the listed claim types, in order, and nothing else.</b> There is no
/// fallback to the default chain: a principal without any listed claim resolves to <c>null</c> rather than
/// to a second identity for the same person. List more than one type to allow an explicit fallback.
/// </para>
/// <para>
/// <b>Why it exists.</b> In Microsoft Entra (workforce and External ID) <c>sub</c> is pairwise — unique per
/// user <i>and application</i> — and OIDC inbound claim mapping copies it into
/// <see cref="ClaimTypes.NameIdentifier"/>, which the default chain reads first. Replacing an app
/// registration therefore re-keys every user, and two applications in one tenant can never share user
/// records. <c>oid</c> is the tenant-wide object id; configure <see cref="DirectoryClaimTypes.ObjectId"/>.
/// </para>
/// <para>
/// <see cref="DirectoryClaimTypes.ObjectId"/> and <see cref="DirectoryClaimTypes.ObjectIdentifier"/> are
/// treated as one claim, so listing either works whether or not inbound claim mapping is on.
/// </para>
/// </remarks>
public sealed class UserIdentityResolver
{
    private static readonly string[][] Aliases =
    [
        [DirectoryClaimTypes.ObjectId, DirectoryClaimTypes.ObjectIdentifier]
    ];

    private readonly string[][] _lookups;

    /// <summary>The unconfigured resolver: today's behaviour at every call site.</summary>
    public static UserIdentityResolver Default { get; } = new();

    /// <summary>Creates the unconfigured resolver.</summary>
    public UserIdentityResolver()
        : this(null)
    {
    }

    /// <summary>Creates a resolver reading the given claim types in order; null or empty means unconfigured.</summary>
    /// <exception cref="ArgumentException">A listed claim type is null or whitespace.</exception>
    public UserIdentityResolver(IEnumerable<string> claimTypes)
    {
        var types = claimTypes?.ToArray() ?? [];
        if (types.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A user identity claim type cannot be null or empty.", nameof(claimTypes));
        }

        ClaimTypes = types;
        _lookups = types.Select(ExpandAliases).ToArray();
    }

    /// <summary>The configured claim types, in order. Empty when unconfigured.</summary>
    public IReadOnlyList<string> ClaimTypes { get; }

    /// <summary>True when claim types were configured, replacing the default resolution everywhere.</summary>
    public bool IsConfigured => ClaimTypes.Count > 0;

    /// <summary>
    /// The identity the user record is stored under (<see cref="IUser.Identity"/>), or null when the
    /// principal carries none.
    /// </summary>
    public string GetUserIdentity(ClaimsPrincipal principal)
    {
        if (principal == null) return null;

        return IsConfigured ? FindConfigured(principal) : principal.GetIdentity().Identity;
    }

    /// <summary>
    /// The exact-matchable subject recorded on audit entries and support cases, or null when the principal
    /// carries none.
    /// </summary>
    public string GetSubject(ClaimsPrincipal principal)
    {
        if (principal == null) return null;

        return IsConfigured
            ? FindConfigured(principal)
            : principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    }

    private string FindConfigured(ClaimsPrincipal principal)
    {
        foreach (var candidates in _lookups)
        {
            foreach (var candidate in candidates)
            {
                var value = principal.Claims
                    .FirstOrDefault(c => string.Equals(c.Type, candidate, StringComparison.OrdinalIgnoreCase))
                    ?.Value;

                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }

        return null;
    }

    private static string[] ExpandAliases(string claimType)
    {
        var group = Aliases.FirstOrDefault(g => g.Contains(claimType, StringComparer.OrdinalIgnoreCase));

        return group == null
            ? [claimType]
            : [claimType, .. group.Where(x => !string.Equals(x, claimType, StringComparison.OrdinalIgnoreCase))];
    }
}
