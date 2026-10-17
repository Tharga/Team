using Tharga.Team;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// How one sign-in method is shown to an administrator: a label and, optionally, an icon.
/// </summary>
/// <param name="Label">The text on the chip, e.g. <c>"e-ID"</c>.</param>
/// <param name="IconUrl">An image shown before the label, or null for none.</param>
public sealed record SignInMethod(string Label, string IconUrl = null);

/// <summary>
/// How the users list names the ways a user signs in. The directory reports identities in its own terms
/// (a sign-in type and an issuer); only the host knows that issuer <c>https://broker.example/</c> is
/// "e-ID", so the names come from here and nothing provider-specific is built into the library.
/// </summary>
/// <remarks>
/// Mappings are tried in the order added and the first that returns a method wins. An identity no mapping
/// claims falls back: a federated identity shows its issuer's host name, an email identity shows "Email",
/// and a <c>userPrincipalName</c> identity is hidden unless <see cref="ShowUserPrincipalName"/> is set —
/// Entra gives every user one, so showing it says nothing about how the user actually signs in.
/// </remarks>
public sealed class SignInMethodOptions
{
    private readonly List<Func<DirectoryUserIdentity, SignInMethod>> _mappings = [];

    /// <summary>The mappings, in the order they are tried.</summary>
    public IReadOnlyList<Func<DirectoryUserIdentity, SignInMethod>> Mappings => _mappings;

    /// <summary>
    /// Adds a mapping from an identity to how it is shown. Return null for an identity the mapping does not
    /// recognise, so the next mapping (or the fallback) gets it.
    /// </summary>
    public SignInMethodOptions Add(Func<DirectoryUserIdentity, SignInMethod> mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        _mappings.Add(mapping);
        return this;
    }

    /// <summary>
    /// Show <c>userPrincipalName</c> identities that no mapping claims. Default off.
    /// </summary>
    public bool ShowUserPrincipalName { get; set; }
}
